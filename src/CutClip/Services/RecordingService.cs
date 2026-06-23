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

        _frameQueue = new BlockingCollection<CaptureFrame>(boundedCapacity: 4);
        _captureCts = new CancellationTokenSource();
        _captureService = new ScreenCaptureService();
        _encoder = new FFmpegEncoderService();

        try
        {
            var encoderStartTask = Task.Run(() =>
            {
                _encoder!.Start(width, height, fps, format, _state.RecordSystemAudio, _state.RecordMicrophone);
            });

            _captureService!.Start(captureRegion, fps, _frameQueue, _captureCts.Token);

            await encoderStartTask.ConfigureAwait(true);

            _tempPath = _encoder.TempOutputPath;

            _encoderTask = Task.Run(() => EncodeLoop(_captureCts.Token), _captureCts.Token);

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

        _state.IsRecording = false;

        try
        {
            _captureCts?.Cancel();
            _captureService?.Stop();
            _frameQueue?.CompleteAdding();

            if (_encoderTask is not null)
            {
                await _encoderTask.ConfigureAwait(false);
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
                RecordingStopped?.Invoke(this, savedPath);
            }
            else
            {
                RecordingFailed?.Invoke(this, "Recording failed to finalize.");
            }

            return savedPath;
        }
        catch (Exception ex)
        {
            RecordingFailed?.Invoke(this, ex.Message);
            return null;
        }
        finally
        {
            DisposeCaptureResources();
        }
    }

    public async Task CancelAsync()
    {
        if (!_state.IsRecording)
        {
            return;
        }

        _state.IsRecording = false;

        try
        {
            _captureCts?.Cancel();
            _captureService?.Stop();
            _frameQueue?.CompleteAdding();

            if (_encoderTask is not null)
            {
                try
                {
                    await _encoderTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected on cancel.
                }
            }

            RecordingCancelled?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            RecordingFailed?.Invoke(this, ex.Message);
        }
        finally
        {
            DisposeCaptureResources();
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
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_isPaused)
                {
                    DiscardQueuedFrames();
                    Thread.Sleep(10);
                    continue;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                DrainLatestFrame(ref holdFrame);

                if (holdFrame is null)
                {
                    if (_frameQueue.IsAddingCompleted)
                    {
                        break;
                    }

                    Thread.Sleep(1);
                    continue;
                }

                var targetTicks = startTicks + GetTotalPauseTicks() + framesWritten * intervalTicks;
                WaitUntilTicks(targetTicks, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                _encoder.WriteFrame(holdFrame);
                framesWritten++;
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
