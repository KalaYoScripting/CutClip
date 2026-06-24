using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace CutClip.Services;

/// <summary>
/// Captures system audio via WASAPI loopback to a sidecar WAV file (muxed after recording).
/// Avoids blocking the video pipe when FFmpeg reads audio over TCP during live encode.
/// </summary>
public sealed class NativeSystemAudioCapture : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private WaveFileWriter? _writer;
    private WaveFormat? _writeFormat;
    private byte[]? _pcm16Buffer;
    private string? _tempWavPath;
    private bool _disposed;
    private volatile bool _paused;

    public string? TempWavPath => _tempWavPath;

    public void Prepare()
    {
        _capture = new WasapiLoopbackCapture();
        _tempWavPath = Path.Combine(Path.GetTempPath(), $"CutClip_{Guid.NewGuid():N}.wav");
        TempFileService.Track(_tempWavPath);
        _writeFormat = new WaveFormat(_capture.WaveFormat.SampleRate, 16, _capture.WaveFormat.Channels);
    }

    public void BeginCapture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_capture is null || _tempWavPath is null || _writeFormat is null)
        {
            throw new InvalidOperationException("Audio capture not prepared.");
        }

        _writer = new WaveFileWriter(_tempWavPath, _writeFormat);
        _capture.DataAvailable += OnDataAvailable;
        _capture.StartRecording();
    }

    public void SignalVideoStarted()
    {
        // Sidecar audio starts with recording; kept for API compatibility.
    }

    public void SetPaused(bool paused) =>
        _paused = paused;

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_paused || e.BytesRecorded <= 0 || _writer is null || _capture is null)
        {
            return;
        }

        try
        {
            if (_capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                var outputBytes = ConvertFloatToPcm16(e.Buffer, e.BytesRecorded);
                _writer.Write(_pcm16Buffer!, 0, outputBytes);
                return;
            }

            _writer.Write(e.Buffer, 0, e.BytesRecorded);
        }
        catch
        {
            // Best-effort while recording.
        }
    }

    private int ConvertFloatToPcm16(byte[] input, int bytesRecorded)
    {
        var sampleCount = bytesRecorded / 4;
        var outputBytes = sampleCount * 2;
        if (_pcm16Buffer is null || _pcm16Buffer.Length < outputBytes)
        {
            _pcm16Buffer = new byte[outputBytes];
        }

        for (var i = 0; i < sampleCount; i++)
        {
            var sample = BitConverter.ToSingle(input, i * 4);
            sample = Math.Clamp(sample, -1f, 1f);
            var pcm = (short)(sample * short.MaxValue);
            _pcm16Buffer[i * 2] = (byte)(pcm & 0xFF);
            _pcm16Buffer[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
        }

        return outputBytes;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            if (_capture.CaptureState == CaptureState.Capturing)
            {
                _capture.StopRecording();
            }

            _capture.Dispose();
            _capture = null;
        }

        _writer?.Dispose();
        _writer = null;
    }
}
