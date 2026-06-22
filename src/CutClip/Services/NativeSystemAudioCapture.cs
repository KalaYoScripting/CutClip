using System.Net;
using System.Net.Sockets;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace CutClip.Services;

/// <summary>
/// Captures system audio via WASAPI loopback and streams it to FFmpeg over TCP.
/// Used when FFmpeg on PATH lacks WASAPI support (Stereo Mix often captures silence).
/// </summary>
public sealed class NativeSystemAudioCapture : IDisposable
{
    private TcpListener? _listener;
    private NetworkStream? _stream;
    private WasapiLoopbackCapture? _capture;
    private Task? _captureTask;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private int _port;

    public WaveFormat WaveFormat { get; private set; } = new(44100, 32, 2);

    public string BuildFfmpegInputArgs() =>
        $"-thread_queue_size 512 -f f32le -ar {WaveFormat.SampleRate} -ac {WaveFormat.Channels} -i tcp://127.0.0.1:{_port} ";

    public void Prepare()
    {
        _capture = new WasapiLoopbackCapture();
        WaveFormat = _capture.WaveFormat;

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    /// <summary>
    /// Waits for FFmpeg to connect, then starts WASAPI loopback capture. Call before FFmpeg process starts.
    /// </summary>
    public void BeginCaptureAfterFfmpegStarted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _cts = new CancellationTokenSource();
        _captureTask = Task.Run(() => CaptureLoop(_cts.Token), _cts.Token);
    }

    private void CaptureLoop(CancellationToken cancellationToken)
    {
        try
        {
            if (_listener is null || _capture is null)
            {
                return;
            }

            using var client = _listener.AcceptTcpClient();
            _stream = client.GetStream();

            _capture.DataAvailable += OnDataAvailable;
            _capture.StartRecording();

            using var waitHandle = cancellationToken.WaitHandle;
            while (!cancellationToken.IsCancellationRequested)
            {
                waitHandle.WaitOne(100);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop.
        }
        catch
        {
            // Capture loop failed; allow finalize path to run.
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            _stream?.Write(e.Buffer, 0, e.BytesRecorded);
        }
        catch (IOException)
        {
            // FFmpeg closed the connection.
        }
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
            _cts?.Cancel();
        }
        catch
        {
            // ignore
        }

        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            if (_capture.CaptureState == CaptureState.Capturing)
            {
                _capture.StopRecording();
            }

            _capture.Dispose();
        }

        try
        {
            _stream?.Close();
        }
        catch
        {
            // ignore
        }

        try
        {
            _listener?.Stop();
        }
        catch
        {
            // ignore
        }

        _cts?.Dispose();
    }
}
