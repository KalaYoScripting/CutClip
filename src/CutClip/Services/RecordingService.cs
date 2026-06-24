using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using CutClip.Models;

namespace CutClip.Services;

public sealed class RecordingService : IDisposable
{
    private readonly AppState _state = AppState.Instance;
    private ScreenCaptureService? _captureService;
    private FFmpegEncoderService? _encoder;
    private BlockingCollection<CaptureFrame>? _frameQueue;
    private CancellationTokenSource? _captureCts;
    private Task? _encoderTask;
    private string? _tempPath;
    private bool _disposed;
    private int _stopInProgress;

    public event EventHandler? RecordingStopping;
    public event EventHandler? RecordingStarted;
    public event EventHandler<string>? RecordingStopped;
    public event EventHandler? RecordingCancelled;
    public event EventHandler<string>? RecordingFailed;
    public event EventHandler? PauseStateChanged;

    public bool IsRecording => _state.IsRecording;
    public bool IsPaused => _isPaused;

    private volatile bool _isPaused;
    private TimeSpan _pausedDuration;
    private DateTime _pauseStart;

    public TimeSpan GetElapsed()
    {
        var elapsed = DateTime.Now - _state.StartTime - _pausedDuration;
        if (_isPaused)
        {
            elapsed -= DateTime.Now - _pauseStart;
        }

        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    public void TogglePause()
    {
        if (!_state.IsRecording)
        {
            return;
        }

        if (_isPaused)
        {
            _pausedDuration += DateTime.Now - _pauseStart;
            _isPaused = false;
        }
        else
        {
            _pauseStart = DateTime.Now;
            _isPaused = true;
        }

        PauseStateChanged?.Invoke(this, EventArgs.Empty);
        _encoder?.SetAudioPaused(_isPaused);
    }

    public async Task StartAsync(Rect region)
    {
        if (_state.IsRecording)
        {
            return;
        }

        if (region.Width < 1 || region.Height < 1)
        {
            RecordingFailed?.Invoke(this, "Invalid capture region.");
            return;
        }

        // H.264/yuv420p requires even width and height.
        var width = Math.Max(2, (int)region.Width & ~1);
        var height = Math.Max(2, (int)region.Height & ~1);
        var captureRegion = new Rect(region.X, region.Y, width, height);

        _state.SelectedRegion = captureRegion;
        _state.StartTime = DateTime.Now;
        _state.OutputFilePath = OutputPathService.GenerateOutputPath(_state.OutputFormat);
        _state.IsRecording = true;
        _isPaused = false;
        _pausedDuration = TimeSpan.Zero;

        var fps = _state.Fps;
        var format = _state.OutputFormat;

        if (AudioCaptureService.IsAudioEnabled(_state.RecordSystemAudio, _state.RecordMicrophone)
            && !AudioCaptureService.TryBuild(
                _state.RecordSystemAudio,
                _state.RecordMicrophone,
                out _,
                out _,
                out _,
                out var audioError))
        {
            _state.IsRecording = false;
            RecordingFailed?.Invoke(this, audioError ?? "Failed to configure audio.");
            return;
        }

        _frameQueue = new BlockingCollection<CaptureFrame>(boundedCapacity: 32);
        _captureCts = new CancellationTokenSource();
        _captureService = new ScreenCaptureService();
        _encoder = new FFmpegEncoderService();

        try
        {
            await Task.Run(() =>
            {
                _encoder!.Start(width, height, fps, format, _state.RecordSystemAudio, _state.RecordMicrophone);
            }).ConfigureAwait(true);

            _tempPath = _encoder.TempOutputPath;

            _encoderTask = Task.Run(() => EncodeLoop(_captureCts.Token), _captureCts.Token);

            _captureService.Start(captureRegion, fps, _frameQueue, _captureCts.Token);

            RecordingStarted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            await CleanupFailedStartAsync().ConfigureAwait(false);
            _state.IsRecording = false;
            RecordingFailed?.Invoke(this, ex.Message);
        }
    }

    public async Task<string?> StopAsync()
    {
        if (!_state.IsRecording)
        {
            return null;
        }

        if (Interlocked.CompareExchange(ref _stopInProgress, 1, 0) != 0)
        {
            return null;
        }

        try
        {
            RecordingStopping?.Invoke(this, EventArgs.Empty);
            SignalStopCapture();

            if (_encoderTask is not null)
            {
                await WaitForEncoderTaskAsync().ConfigureAwait(false);
            }

            var finalPath = _state.OutputFilePath;
            var savedPath = await (_encoder?.StopAsync(finalPath) ?? Task.FromResult<string?>(null))
                .ConfigureAwait(false);

            if (savedPath is not null)
            {
                var metadata = new RecordingMetadata
                {
                    FileName = Path.GetFileName(savedPath),
                    FullPath = savedPath,
                    Duration = GetElapsed(),
                    Region = _state.SelectedRegion,
                    CreatedAt = _state.StartTime,
                    Fps = _state.Fps,
                    Format = _state.OutputFormat
                };

                RecentRecordingsService.Add(metadata);
                InvokeRecordingStopped(savedPath);
            }
            else
            {
                var detail = _encoder?.GetLastErrorSummary();
                var message = string.IsNullOrWhiteSpace(detail)
                    ? "Recording failed to finalize."
                    : $"Recording failed to finalize: {detail}";
                InvokeRecordingFailed(message);
            }

            return savedPath;
        }
        catch (Exception ex)
        {
            InvokeRecordingFailed(ex.Message);
            return null;
        }
        finally
        {
            _state.IsRecording = false;
            Interlocked.Exchange(ref _stopInProgress, 0);
            DisposeCaptureResources();
        }
    }

    public async Task CancelAsync()
    {
        if (!_state.IsRecording)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _stopInProgress, 1, 0) != 0)
        {
            return;
        }

        try
        {
            RecordingStopping?.Invoke(this, EventArgs.Empty);
            SignalStopCapture();

            if (_encoderTask is not null)
            {
                await WaitForEncoderTaskAsync().ConfigureAwait(false);
            }

            InvokeRecordingCancelled();
        }
        catch (Exception ex)
        {
            InvokeRecordingFailed(ex.Message);
        }
        finally
        {
            _state.IsRecording = false;
            Interlocked.Exchange(ref _stopInProgress, 0);
            DisposeCaptureResources();
        }
    }

    private void InvokeRecordingStopped(string path)
    {
        try
        {
            RecordingStopped?.Invoke(this, path);
        }
        catch
        {
            // Save already completed; UI handlers must not affect the result.
        }
    }

    private void InvokeRecordingFailed(string message)
    {
        try
        {
            RecordingFailed?.Invoke(this, message);
        }
        catch
        {
            // ignore UI handler errors
        }
    }

    private void InvokeRecordingCancelled()
    {
        try
        {
            RecordingCancelled?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // ignore UI handler errors
        }
    }

    private void SignalStopCapture()
    {
        _captureService?.Stop();
        _frameQueue?.CompleteAdding();
        _captureCts?.Cancel();
    }

    private async Task WaitForEncoderTaskAsync()
    {
        if (_encoderTask is null)
        {
            return;
        }

        var completed = await Task.WhenAny(_encoderTask, Task.Delay(TimeSpan.FromSeconds(30)))
            .ConfigureAwait(false);

        if (completed != _encoderTask)
        {
            return;
        }

        try
        {
            await _encoderTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on cancel.
        }
    }

    private void EncodeLoop(CancellationToken cancellationToken)
    {
        if (_frameQueue is null || _encoder is null)
        {
            return;
        }

        var fps = Math.Max(1, _state.Fps);
        var intervalTicks = Stopwatch.Frequency / fps;
        var startTicks = Stopwatch.GetTimestamp();
        long framesWritten = 0;
        CaptureFrame? holdFrame = null;

        try
        {
            while (true)
            {
                var stopRequested = cancellationToken.IsCancellationRequested
                    || (_frameQueue?.IsAddingCompleted ?? false);

                if (_isPaused && !stopRequested)
                {
                    DiscardQueuedFrames();
                    if (cancellationToken.WaitHandle.WaitOne(10))
                    {
                        stopRequested = true;
                    }
                    else
                    {
                        continue;
                    }
                }

                DrainLatestFrame(ref holdFrame);

                if (holdFrame is null)
                {
                    if (stopRequested)
                    {
                        break;
                    }

                    if (cancellationToken.WaitHandle.WaitOne(1))
                    {
                        continue;
                    }

                    continue;
                }

                if (!stopRequested)
                {
                    var targetTicks = startTicks + GetTotalPauseTicks() + framesWritten * intervalTicks;
                    WaitUntilTicks(targetTicks, cancellationToken);
                    stopRequested = cancellationToken.IsCancellationRequested
                        || (_frameQueue?.IsAddingCompleted ?? false);
                }

                try
                {
                    _encoder.EnqueueFrame(holdFrame, cancellationToken);
                    framesWritten++;
                    holdFrame = null;
                }
                catch (OperationCanceledException)
                {
                    holdFrame?.Dispose();
                    holdFrame = null;
                    break;
                }

                if (stopRequested && holdFrame is null && _frameQueue!.Count == 0)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop.
        }
        catch (Exception)
        {
            // FFmpeg may exit before stop; allow finalize path to run.
        }
        finally
        {
            holdFrame?.Dispose();
            _encoder?.CompleteFrameWriting();
        }
    }

    private void DrainLatestFrame(ref CaptureFrame? holdFrame)
    {
        while (_frameQueue!.TryTake(out var frame))
        {
            holdFrame?.Dispose();
            holdFrame = frame;
        }
    }

    private void DiscardQueuedFrames()
    {
        while (_frameQueue!.TryTake(out var frame))
        {
            frame.Dispose();
        }
    }

    private long GetTotalPauseTicks()
    {
        var paused = _pausedDuration;
        if (_isPaused)
        {
            paused += DateTime.Now - _pauseStart;
        }

        return (long)(paused.TotalSeconds * Stopwatch.Frequency);
    }

    private static void WaitUntilTicks(long targetTicks, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = Stopwatch.GetTimestamp();
            if (now >= targetTicks)
            {
                return;
            }

            var remainingTicks = targetTicks - now;
            if (remainingTicks > Stopwatch.Frequency / 500)
            {
                var remainingMs = remainingTicks * 1000 / Stopwatch.Frequency;
                Thread.Sleep(remainingMs > 0 ? Math.Min((int)remainingMs, 16) : 1);
            }
            else
            {
                Thread.SpinWait(50);
            }
        }
    }

    private async Task CleanupFailedStartAsync()
    {
        _captureCts?.Cancel();
        _frameQueue?.CompleteAdding();
        DisposeCaptureResources();
        await Task.CompletedTask;
    }

    private void DisposeCaptureResources()
    {
        _captureService?.Dispose();
        _captureService = null;
        _encoder?.Dispose();
        _encoder = null;
        _frameQueue?.Dispose();
        _frameQueue = null;
        _captureCts?.Dispose();
        _captureCts = null;
        _encoderTask = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_state.IsRecording)
        {
            StopAsync().GetAwaiter().GetResult();
        }
        else
        {
            DisposeCaptureResources();
        }
    }
}
