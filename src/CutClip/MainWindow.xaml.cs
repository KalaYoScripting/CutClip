using System.Diagnostics;
using System.Windows;
using CutClip.Interop;
using CutClip.Models;
using CutClip.Services;
using CutClip.Views;
using Hardcodet.Wpf.TaskbarNotification;

namespace CutClip;

public partial class MainWindow : Window
{
    private readonly AppState _state = AppState.Instance;
    private readonly RecordingService _recordingService = new();
    private HotkeyManager? _hotkeyManager;
    private HotkeyHostWindow? _hotkeyHostWindow;
    private RecordingOverlay? _recordingOverlay;
    private CountdownOverlay? _countdownOverlay;
    private string? _lastSavedRecordingPath;
    private bool _lastBalloonIsRecordingSaved;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        InitializeApp();
    }

    private void InitializeApp()
    {
        if (!ScreenCaptureServiceSupported())
        {
            System.Windows.MessageBox.Show(
                "CutClip requires Windows 10 version 1903 or later for screen capture.",
                "CutClip",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        _hotkeyHostWindow = new HotkeyHostWindow();
        _hotkeyHostWindow.Show();

        _hotkeyManager = new HotkeyManager(_hotkeyHostWindow);
        _hotkeyManager.HotkeyPressed += (_, _) => Dispatcher.Invoke(ToggleRecording);
        RegisterHotkey();

        _recordingService.RecordingStarted += (_, _) => Dispatcher.Invoke(OnRecordingStarted);
        _recordingService.RecordingStopped += (_, path) => Dispatcher.Invoke(() => OnRecordingStopped(path));
        _recordingService.RecordingCancelled += (_, _) => Dispatcher.Invoke(OnRecordingCancelled);
        _recordingService.RecordingFailed += (_, message) => Dispatcher.Invoke(() => OnRecordingFailed(message));
        _recordingService.PauseStateChanged += (_, _) => Dispatcher.Invoke(OnPauseStateChanged);
    }

    private RecordingOverlay RecordingOverlay =>
        _recordingOverlay ??= CreateRecordingOverlay();

    private CountdownOverlay CountdownOverlay =>
        _countdownOverlay ??= new CountdownOverlay();

    private RecordingOverlay CreateRecordingOverlay()
    {
        var overlay = new RecordingOverlay();
        overlay.PauseToggleRequested += (_, _) => _recordingService.TogglePause();
        overlay.SaveRequested += (_, _) => StopRecording();
        overlay.CancelRequested += (_, _) => CancelRecording();
        return overlay;
    }

    private static bool ScreenCaptureServiceSupported()
    {
        try
        {
            return Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported();
        }
        catch
        {
            return false;
        }
    }

    private void RegisterHotkey()
    {
        if (_hotkeyManager is null)
        {
            return;
        }

        if (!_hotkeyManager.Register(_state.HotkeyVirtualKey, _state.HotkeyModifiers))
        {
            ShowNotification(
                "CutClip",
                "Failed to register global hotkey. It may be in use by another application.",
                BalloonIcon.Warning);
        }
    }

    private void ShowNotification(string title, string message, BalloonIcon icon)
    {
        if (_state.DisableNotifications)
        {
            return;
        }

        TrayIcon.ShowBalloonTip(title, message, icon);
    }

    private void ToggleRecording()
    {
        if (_state.IsRecording)
        {
            StopRecording();
        }
        else
        {
            StartRegionSelection();
        }
    }

    private void SelectRegion_Click(object sender, RoutedEventArgs e)
    {
        StartRegionSelection();
    }

    private async void StartRegionSelection()
    {
        if (_state.IsRecording)
        {
            return;
        }

        var selector = new RegionSelectorOverlay();
        var result = selector.ShowDialog();

        if (result != true || selector.SelectedRegion is not { } region)
        {
            return;
        }

        try
        {
            if (_state.CountdownSeconds > 0)
            {
                await RunCountdownAsync(region).ConfigureAwait(true);
            }

            await _recordingService.StartAsync(region).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            OnRecordingFailed(ex.Message);
        }
    }

    private async Task RunCountdownAsync(Rect region)
    {
        var overlay = CountdownOverlay;
        overlay.PositionAt(region);
        overlay.Show();

        for (var i = _state.CountdownSeconds; i > 0; i--)
        {
            overlay.SetCount(i);
            await Task.Delay(1000).ConfigureAwait(true);
        }

        overlay.Hide();
    }

    private void StopRecording_Click(object sender, RoutedEventArgs e)
    {
        StopRecording();
    }

    private async void StopRecording()
    {
        if (!_state.IsRecording)
        {
            return;
        }

        await _recordingService.StopAsync().ConfigureAwait(true);
    }

    private async void CancelRecording()
    {
        if (!_state.IsRecording)
        {
            return;
        }

        await _recordingService.CancelAsync().ConfigureAwait(true);
    }

    private void OnRecordingStarted()
    {
        StopMenuItem.IsEnabled = true;
        RecordingOverlay.ShowForRegion(
            _state.SelectedRegion,
            _state.StartTime,
            () => _recordingService.GetElapsed());
        ShowNotification("CutClip", $"Recording started. Press {_state.Hotkey.DisplayName} or use tray to stop.", BalloonIcon.Info);
    }

    private void OnPauseStateChanged()
    {
        RecordingOverlay.SetPaused(_recordingService.IsPaused);
    }

    private void OnRecordingStopped(string path)
    {
        StopMenuItem.IsEnabled = false;
        RecordingOverlay.HideOverlay();

        if (_state.CopyRecordingToClipboard)
        {
            ClipboardService.CopyFile(path);
        }

        _lastSavedRecordingPath = path;
        _lastBalloonIsRecordingSaved = true;
        ShowNotification("CutClip", $"Recording saved — click to show in Explorer:\n{path}", BalloonIcon.Info);
    }

    private void OnRecordingCancelled()
    {
        StopMenuItem.IsEnabled = false;
        RecordingOverlay.HideOverlay();
        _lastBalloonIsRecordingSaved = false;
    }

    private void OnRecordingFailed(string message)
    {
        StopMenuItem.IsEnabled = false;
        RecordingOverlay.HideOverlay();
        _lastBalloonIsRecordingSaved = false;
        ShowNotification("CutClip", message, BalloonIcon.Error);
    }

    private void TrayIcon_TrayBalloonTipClicked(object sender, RoutedEventArgs e)
    {
        if (!_lastBalloonIsRecordingSaved || string.IsNullOrWhiteSpace(_lastSavedRecordingPath))
        {
            return;
        }

        ExplorerHelper.SelectFileInExplorer(_lastSavedRecordingPath);
        _lastBalloonIsRecordingSaved = false;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow();
        if (settings.ShowDialog() == true)
        {
            _hotkeyManager?.Unregister();
            RegisterHotkey();
        }
    }

    private void RecentRecordings_Click(object sender, RoutedEventArgs e)
    {
        var recent = new RecentRecordingsWindow();
        recent.ShowDialog();
    }

    private void OpenVideosFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = OutputPathService.GetVideosFolder();
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
    {
        ToggleRecording();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _hotkeyManager?.Dispose();
        _hotkeyHostWindow?.Close();
        _recordingService.Dispose();
        RecordingOverlay.HideOverlay();
        _recordingOverlay?.Close();
        _countdownOverlay?.Close();
        TempFileService.CleanupAll();
        TrayIcon.Dispose();
    }
}
