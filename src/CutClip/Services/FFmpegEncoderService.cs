using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;

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
    private bool _useSidecarAudio;
    private string? _sidecarWavPath;
    private readonly StringBuilder _lastFfmpegError = new();
    private string? _lastMuxError;
    private BlockingCollection<CaptureFrame>? _writeQueue;
    private Task? _writeTask;

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
            _useSidecarAudio = true;
            _nativeSystemAudio = new NativeSystemAudioCapture();
            _nativeSystemAudio.Prepare();
            _sidecarWavPath = _nativeSystemAudio.TempWavPath;
            _nativeSystemAudio.BeginCapture();
        }

        _tempOutputPath = OutputPathService.GenerateTempPath(format);
        TempFileService.Track(_tempOutputPath);

        var args = format.ToLowerInvariant() switch
        {
            "webm" => BuildWebmArgs(),
            "gif" => BuildGifArgs(),
            _ => BuildMp4Args()
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = FfmpegLocator.ResolveExecutable(),
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
        _stderrDrainTask = DrainStderrAsync(_ffmpegProcess.StandardError);

        _writeQueue = new BlockingCollection<CaptureFrame>(boundedCapacity: 64);
        _writeTask = Task.Run(WriteLoop);
    }

    private void WriteLoop()
    {
        if (_writeQueue is null)
        {
            return;
        }

        try
        {
            foreach (var frame in _writeQueue.GetConsumingEnumerable())
            {
                if (_stdin is null || _disposed)
                {
                    frame.Dispose();
                    continue;
                }

                try
                {
                    _stdin.Write(frame.Data, 0, frame.ByteLength);
                }
                catch (IOException)
                {
                    CloseVideoInput();
                }
                finally
                {
                    frame.Dispose();
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Queue completed.
        }
    }

    private async Task DrainStderrAsync(StreamReader reader)
    {
        try
        {
            var buffer = new char[4096];
            int read;
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                lock (_lastFfmpegError)
                {
                    if (_lastFfmpegError.Length < 16_384)
                    {
                        _lastFfmpegError.Append(buffer, 0, read);
                    }
                }
            }
        }
        catch
        {
            // ignore drain errors
        }
    }

    private string BuildVideoInputArgs()
    {
        return $"-f rawvideo -fflags nobuffer -pixel_format bgra -video_size {_width}x{_height} -framerate {_fps} -i pipe:0 ";
    }

    private string BuildVideoOutputArgs()
    {
        return $"-c:v libx264 -preset ultrafast -pix_fmt yuv420p -r {_fps} -threads 0 ";
    }

    private string BuildMp4Args()
    {
        if (_useSidecarAudio)
        {
            if (_recordMicrophone && !string.IsNullOrEmpty(_audioInput))
            {
                return $"-y -thread_queue_size 512 {BuildVideoInputArgs()}{_audioInput}{BuildVideoOutputArgs()}{_audioMap}-max_muxing_queue_size 1024 \"{_tempOutputPath}\"";
            }

            return $"-y -thread_queue_size 512 {BuildVideoInputArgs()}{BuildVideoOutputArgs()}-an \"{_tempOutputPath}\"";
        }

        return $"-y -thread_queue_size 512 {BuildVideoInputArgs()}{_nativeAudioInput}{_audioInput}{BuildVideoOutputArgs()}{_audioMap}-max_muxing_queue_size 1024 \"{_tempOutputPath}\"";
    }

    private string BuildWebmArgs()
    {
        return $"-y -thread_queue_size 128 {BuildVideoInputArgs()}{_nativeAudioInput}{_audioInput}-c:v libvpx-vp9 -deadline realtime -cpu-used 8 -pix_fmt yuv420p -r {_fps} {_audioMap}\"{_tempOutputPath}\"";
    }

    private string BuildGifArgs()
    {
        return $"-y {BuildVideoInputArgs()}-vf \"fps={Math.Min(_fps, 15)},scale={_width}:{_height}:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" \"{_tempOutputPath}\"";
    }

    public void SetAudioPaused(bool paused) =>
        _nativeSystemAudio?.SetPaused(paused);

    public void CloseVideoInput()
    {
        try
        {
            _stdin?.Close();
        }
        catch (IOException)
        {
            // FFmpeg may have already exited.
        }

        _stdin = null;
    }

    public void EnqueueFrame(CaptureFrame frame, CancellationToken cancellationToken)
    {
        if (_disposed || _writeQueue is null || _writeQueue.IsAddingCompleted)
        {
            frame.Dispose();
            return;
        }

        try
        {
            _writeQueue.Add(frame, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            frame.Dispose();
            throw;
        }
        catch (InvalidOperationException)
        {
            frame.Dispose();
        }
    }

    public bool TryEnqueueFrame(CaptureFrame frame, int timeoutMs = 500)
    {
        if (_disposed || _writeQueue is null || _writeQueue.IsAddingCompleted)
        {
            return false;
        }

        if (timeoutMs <= 0)
        {
            return _writeQueue.TryAdd(frame, 0);
        }

        return _writeQueue.TryAdd(frame, TimeSpan.FromMilliseconds(timeoutMs));
    }

    public void CompleteFrameWriting()
    {
        try
        {
            _writeQueue?.CompleteAdding();
        }
        catch (InvalidOperationException)
        {
            // Already completed.
        }
    }

    public async Task WaitForWriterAsync()
    {
        if (_writeTask is null)
        {
            return;
        }

        var completed = await Task.WhenAny(_writeTask, Task.Delay(TimeSpan.FromSeconds(10)))
            .ConfigureAwait(false);

        if (completed != _writeTask)
        {
            CloseVideoInput();
            await Task.WhenAny(_writeTask, Task.Delay(TimeSpan.FromSeconds(5)))
                .ConfigureAwait(false);
        }
        else
        {
            try
            {
                await _writeTask.ConfigureAwait(false);
            }
            catch
            {
                // Writer exits when stdin closes or the queue completes.
            }
        }
    }

    public async Task<string?> StopAsync(string finalOutputPath)
    {
        if (_tempOutputPath is null)
        {
            return null;
        }

        try
        {
            CompleteFrameWriting();
            await WaitForWriterAsync().ConfigureAwait(false);

            var process = _ffmpegProcess;
            if (process is not null)
            {
                try
                {
                    _stdin?.Close();
                }
                catch (IOException)
                {
                    // FFmpeg may have already exited.
                }

                try
                {
                    if (!process.HasExited)
                    {
                        await process.WaitForExitAsync().ConfigureAwait(false);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Process handle already released.
                }

                if (_stdoutDrainTask is not null)
                {
                    await _stdoutDrainTask.ConfigureAwait(false);
                }

                if (_stderrDrainTask is not null)
                {
                    await _stderrDrainTask.ConfigureAwait(false);
                }
            }
            else
            {
                _nativeSystemAudio?.Dispose();
                _nativeSystemAudio = null;
            }

            var tempExists = File.Exists(_tempOutputPath);
            var tempSize = tempExists ? new FileInfo(_tempOutputPath).Length : 0;

            if (!tempExists || tempSize < 512)
            {
                TempFileService.DeleteIfExists(_tempOutputPath);
                return null;
            }

            process?.Dispose();
            _ffmpegProcess = null;
            _stdin = null;

            var sidecarWav = _useSidecarAudio ? _sidecarWavPath : null;
            _nativeSystemAudio?.Dispose();
            _nativeSystemAudio = null;

            Directory.CreateDirectory(Path.GetDirectoryName(finalOutputPath)!);

            if (File.Exists(finalOutputPath))
            {
                File.Delete(finalOutputPath);
            }

            if (sidecarWav is not null && File.Exists(sidecarWav) && new FileInfo(sidecarWav).Length > 44)
            {
                var muxResult = await MuxVideoAndSidecarAsync(
                        _tempOutputPath,
                        sidecarWav,
                        finalOutputPath,
                        _recordMicrophone)
                    .ConfigureAwait(false);

                if (muxResult.Success && File.Exists(finalOutputPath) && new FileInfo(finalOutputPath).Length >= 512)
                {
                    TempFileService.DeleteIfExists(_tempOutputPath);
                    TempFileService.DeleteIfExists(sidecarWav);
                    TempFileService.Untrack(_tempOutputPath);
                    return finalOutputPath;
                }

                _lastMuxError = muxResult.Error;
                TempFileService.DeleteIfExists(finalOutputPath);
                TempFileService.DeleteIfExists(sidecarWav);
                // Fall through to save video-only so the recording is still playable.
            }

            File.Move(_tempOutputPath, finalOutputPath);
            TempFileService.Untrack(_tempOutputPath);
            return finalOutputPath;
        }
        finally
        {
            _nativeSystemAudio?.Dispose();
            _nativeSystemAudio = null;
            _ffmpegProcess?.Dispose();
            _ffmpegProcess = null;
            _stdin = null;
            _disposed = true;
        }
    }

    public string? GetLastErrorSummary()
    {
        if (!string.IsNullOrWhiteSpace(_lastMuxError))
        {
            return _lastMuxError;
        }

        lock (_lastFfmpegError)
        {
            if (_lastFfmpegError.Length == 0)
            {
                return null;
            }

            var text = _lastFfmpegError.ToString();
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return lines.Length > 0 ? lines[^1] : text.Trim();
        }
    }

    private readonly record struct MuxResult(bool Success, int ExitCode, string Attempt, string? Error);

    private static async Task<MuxResult> MuxVideoAndSidecarAsync(
        string videoPath,
        string sidecarWavPath,
        string outputPath,
        bool videoHasMicTrack)
    {
        var attempts = new (string Name, string Args)[]
        {
            ("faststart", BuildMuxArgs(videoPath, sidecarWavPath, outputPath, videoHasMicTrack, fastStart: true)),
            ("plain", BuildMuxArgs(videoPath, sidecarWavPath, outputPath, videoHasMicTrack, fastStart: false)),
            ("reencode", BuildMuxArgs(videoPath, sidecarWavPath, outputPath, videoHasMicTrack, fastStart: false, reencodeVideo: true))
        };

        string? lastError = null;
        foreach (var attempt in attempts)
        {
            TempFileService.DeleteIfExists(outputPath);

            var result = await RunMuxAttemptAsync(attempt.Args).ConfigureAwait(false);
            lastError = result.Error;

            if (result.ExitCode == 0
                && File.Exists(outputPath)
                && new FileInfo(outputPath).Length >= 512)
            {
                return new MuxResult(true, result.ExitCode, attempt.Name, null);
            }
        }

        TempFileService.DeleteIfExists(outputPath);
        return new MuxResult(false, -1, attempts[^1].Name, lastError);
    }

    private static string BuildMuxArgs(
        string videoPath,
        string sidecarWavPath,
        string outputPath,
        bool videoHasMicTrack,
        bool fastStart,
        bool reencodeVideo = false)
    {
        var fastStartFlag = fastStart ? " -movflags +faststart" : string.Empty;
        var videoCodec = reencodeVideo
            ? "-c:v libx264 -preset ultrafast -pix_fmt yuv420p"
            : "-c:v copy";

        if (videoHasMicTrack)
        {
            return $"-y -i \"{videoPath}\" -i \"{sidecarWavPath}\" -filter_complex \"[0:a][1:a]amix=inputs=2:duration=shortest[a];[a]aresample=async=1:first_pts=0[aout]\" -map 0:v:0 -map \"[aout]\" {videoCodec} -c:a aac -b:a 192k{fastStartFlag} \"{outputPath}\"";
        }

        return $"-y -i \"{videoPath}\" -i \"{sidecarWavPath}\" -map 0:v:0 -map 1:a:0 {videoCodec} -af aresample=async=1:first_pts=0 -c:a aac -b:a 192k -shortest{fastStartFlag} \"{outputPath}\"";
    }

    private static async Task<(int ExitCode, string? Error)> RunMuxAttemptAsync(string args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = FfmpegLocator.ResolveExecutable(),
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return (-1, "Failed to start FFmpeg mux process.");
        }

        var stderrTask = process.StandardError.ReadToEndAsync();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        await stdoutTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var summary = lines.LastOrDefault(line =>
                line.Contains("Error", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Invalid", StringComparison.OrdinalIgnoreCase)
                || line.Contains("failed", StringComparison.OrdinalIgnoreCase));
            return (process.ExitCode, summary ?? lines.LastOrDefault() ?? stderr.Trim());
        }

        return (0, null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        CompleteFrameWriting();

        try
        {
            _writeTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignore
        }

        while (_writeQueue?.TryTake(out var frame) == true)
        {
            frame.Dispose();
        }

        _writeQueue?.Dispose();
        _writeQueue = null;
        _writeTask = null;

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
        _ffmpegProcess = null;
        _nativeSystemAudio?.Dispose();
        _nativeSystemAudio = null;

        if (_tempOutputPath is not null)
        {
            TempFileService.DeleteIfExists(_tempOutputPath);
        }
    }
}
