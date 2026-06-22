using System.Diagnostics;
using System.IO;
using CutClip.Models;
using FFMpegCore;

namespace CutClip.Services;

public sealed class FFmpegEncoderService : IDisposable
{
    private Process? _ffmpegProcess;
    private Stream? _stdin;
    private Task? _stderrDrainTask;
    private Task? _stdoutDrainTask;
    private string? _tempOutputPath;
    private int _width;
    private int _height;
    private int _fps;
    private bool _recordSystemAudio;
    private bool _recordMicrophone;
    private string _audioInput = string.Empty;
    private string _nativeAudioInput = string.Empty;
    private string _audioMap = string.Empty;
    private bool _disposed;
    private NativeSystemAudioCapture? _nativeSystemAudio;
    private int _videoFramesWritten;

    public string TempOutputPath => _tempOutputPath ?? throw new InvalidOperationException("Encoder not started.");

    public void Start(int width, int height, int fps, string format = "mp4", bool recordSystemAudio = false, bool recordMicrophone = false)
    {
        _width = width;
        _height = height;
        _fps = fps;
        _recordSystemAudio = recordSystemAudio;
        _recordMicrophone = recordMicrophone;

        if (!AudioCaptureService.TryBuild(recordSystemAudio, recordMicrophone, out _audioInput, out _audioMap, out var useNativeSystemLoopback, out var audioError))
        {
            throw new InvalidOperationException(audioError ?? "Failed to configure audio capture.");
        }

        if (useNativeSystemLoopback)
        {
            _nativeSystemAudio = new NativeSystemAudioCapture();
            _nativeSystemAudio.Prepare();
            _nativeAudioInput = _nativeSystemAudio.BuildFfmpegInputArgs();
            _nativeSystemAudio.BeginCaptureAfterFfmpegStarted();
        }

        _tempOutputPath = OutputPathService.GenerateTempPath(format);
        TempFileService.Track(_tempOutputPath);

        ConfigureFFmpegPath();

        var args = format.ToLowerInvariant() switch
        {
            "webm" => BuildWebmArgs(),
            "gif" => BuildGifArgs(),
            _ => BuildMp4Args()
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        _ffmpegProcess = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start FFmpeg. Ensure FFmpeg is installed and on PATH.");

        _stdin = _ffmpegProcess.StandardInput.BaseStream;

        // Continuously drain stdout/stderr so FFmpeg never blocks on a full pipe buffer.
        _stdoutDrainTask = _ffmpegProcess.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        _stderrDrainTask = DrainReaderAsync(_ffmpegProcess.StandardError);
    }

    private static async Task DrainReaderAsync(StreamReader reader)
    {
        try
        {
            await reader.ReadToEndAsync().ConfigureAwait(false);
        }
        catch
        {
            // ignore drain errors
        }
    }

    private string BuildVideoInputArgs()
    {
        return $"-f rawvideo -pixel_format bgra -video_size {_width}x{_height} -framerate {_fps} -i pipe:0 ";
    }

    private string BuildVideoOutputArgs()
    {
        return $"-c:v libx264 -preset ultrafast -pix_fmt yuv420p -r {_fps} ";
    }

    private string BuildMp4Args()
    {
        return $"-y -thread_queue_size 1024 {BuildVideoInputArgs()}{_nativeAudioInput}{_audioInput}{BuildVideoOutputArgs()}{_audioMap}-movflags +faststart \"{_tempOutputPath}\"";
    }

    private string BuildWebmArgs()
    {
        return $"-y -thread_queue_size 1024 {BuildVideoInputArgs()}{_nativeAudioInput}{_audioInput}-c:v libvpx-vp9 -pix_fmt yuv420p -r {_fps} {_audioMap}\"{_tempOutputPath}\"";
    }

    private string BuildGifArgs()
    {
        return $"-y {BuildVideoInputArgs()}-vf \"fps={Math.Min(_fps, 15)},scale={_width}:{_height}:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" \"{_tempOutputPath}\"";
    }

    public void SetAudioPaused(bool paused) =>
        _nativeSystemAudio?.SetPaused(paused);

    public void WriteFrame(CaptureFrame frame)
    {
        if (_stdin is null || _disposed)
        {
            return;
        }

        if (Interlocked.Increment(ref _videoFramesWritten) == 1)
        {
            _nativeSystemAudio?.SignalVideoStarted();
        }

        _stdin.Write(frame.Data, 0, frame.Data.Length);
    }

    public async Task<string?> StopAsync(string finalOutputPath)
    {
        if (_ffmpegProcess is null || _tempOutputPath is null)
        {
            return null;
        }

        try
        {
            try
            {
                _stdin?.Close();
            }
            catch (IOException)
            {
                // FFmpeg may have already exited.
            }

            _nativeSystemAudio?.Dispose();
            _nativeSystemAudio = null;

            await _ffmpegProcess.WaitForExitAsync().ConfigureAwait(false);

            if (_stdoutDrainTask is not null)
            {
                await _stdoutDrainTask.ConfigureAwait(false);
            }

            if (_stderrDrainTask is not null)
            {
                await _stderrDrainTask.ConfigureAwait(false);
            }

            if (_ffmpegProcess.ExitCode != 0
                || !File.Exists(_tempOutputPath)
                || new FileInfo(_tempOutputPath).Length < 512)
            {
                TempFileService.DeleteIfExists(_tempOutputPath);
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(finalOutputPath)!);

            if (File.Exists(finalOutputPath))
            {
                File.Delete(finalOutputPath);
            }

            File.Move(_tempOutputPath, finalOutputPath);
            TempFileService.Untrack(_tempOutputPath);
            return finalOutputPath;
        }
        finally
        {
            _nativeSystemAudio?.Dispose();
            _nativeSystemAudio = null;
            _ffmpegProcess.Dispose();
            _ffmpegProcess = null;
            _stdin = null;
        }
    }

    private static void ConfigureFFmpegPath()
    {
        GlobalFFOptions.Configure(options =>
        {
            options.BinaryFolder = FindFFmpegDirectory() ?? options.BinaryFolder;
        });
    }

    private static string? FindFFmpegDirectory()
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in paths)
        {
            var candidate = Path.Combine(dir, "ffmpeg.exe");
            if (File.Exists(candidate))
            {
                return dir;
            }
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var bundled = Path.Combine(localAppData, "CutClip", "ffmpeg");
        if (File.Exists(Path.Combine(bundled, "ffmpeg.exe")))
        {
            return bundled;
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _stdin?.Close();
            if (_ffmpegProcess is { HasExited: false })
            {
                _ffmpegProcess.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignore
        }

        _ffmpegProcess?.Dispose();
        _nativeSystemAudio?.Dispose();
        _nativeSystemAudio = null;

        if (_tempOutputPath is not null)
        {
            TempFileService.DeleteIfExists(_tempOutputPath);
        }
    }
}
