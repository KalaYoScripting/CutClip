using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CutClip.Interop;

namespace CutClip.Views;

public partial class RecordingOverlay : Window
{
    private const double BorderGapPhysical = 16;
    private const double BorderStroke = 3;

    private readonly DispatcherTimer _timer;
    private Window? _borderWindow;
    private Rect _physicalRegion;
    private double _windowScale = 1.0;
    private Func<TimeSpan>? _getElapsed;

    public event EventHandler? PauseToggleRequested;
    public event EventHandler? SaveRequested;

    public RecordingOverlay()
    {
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _timer.Tick += (_, _) => UpdateTimerDisplay();
    }

    public void ShowForRegion(Rect physicalRegion, DateTime startTime, Func<TimeSpan> getElapsed)
    {
        _physicalRegion = physicalRegion;
        PauseButton.Content = "Pause";
        _getElapsed = getElapsed;

        _windowScale = DpiHelper.GetScaleForPoint(
            (int)(physicalRegion.Left + physicalRegion.Width / 2),
            (int)(physicalRegion.Top + physicalRegion.Height / 2));

        ShowRegionBorder();

        Show();
        UpdateLayout();
        PositionToolbar();

        _timer.Start();
        UpdateTimerDisplay();
    }

    public void SetPaused(bool paused) =>
        PauseButton.Content = paused ? "Resume" : "Pause";

    public void HideOverlay()
    {
        _timer.Stop();
        Hide();
        CloseBorderWindow();
    }

    protected override void OnClosed(EventArgs e)
    {
        CloseBorderWindow();
        base.OnClosed(e);
    }

    private void Window_SourceInitialized(object? sender, EventArgs e) =>
        ApplyNoActivate(this);

    private void ShowRegionBorder()
    {
        CloseBorderWindow();

        var dipLeft = _physicalRegion.X / _windowScale;
        var dipTop = _physicalRegion.Y / _windowScale;
        var dipWidth = _physicalRegion.Width / _windowScale;
        var dipHeight = _physicalRegion.Height / _windowScale;

        var gap = BorderGapPhysical / _windowScale;
        var rectWidth = dipWidth + 2 * (gap + BorderStroke);
        var rectHeight = dipHeight + 2 * (gap + BorderStroke);
        var rectLeft = dipLeft - gap - BorderStroke;
        var rectTop = dipTop - gap - BorderStroke;

        _borderWindow = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            Left = rectLeft - BorderStroke / 2,
            Top = rectTop - BorderStroke / 2,
            Width = rectWidth + BorderStroke,
            Height = rectHeight + BorderStroke
        };

        _borderWindow.Content = new Rectangle
        {
            Width = rectWidth,
            Height = rectHeight,
            Stroke = new SolidColorBrush(Color.FromRgb(255, 140, 0)),
            StrokeThickness = BorderStroke,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };

        _borderWindow.SourceInitialized += (_, _) => ApplyNoActivate(_borderWindow);
        _borderWindow.Show();
    }

    private void CloseBorderWindow()
    {
        _borderWindow?.Close();
        _borderWindow = null;
    }

    private void PositionToolbar()
    {
        const double toolbarMargin = 12;
        const double minOutsideMargin = 4;

        var dipLeft = _physicalRegion.X / _windowScale;
        var dipTop = _physicalRegion.Y / _windowScale;
        var dipWidth = _physicalRegion.Width / _windowScale;
        var dipHeight = _physicalRegion.Height / _windowScale;
        var dipBottom = dipTop + dipHeight;

        var virtualScreen = MonitorHelper.GetVirtualScreenBounds();
        var screenScale = DpiHelper.GetScaleForPoint(
            (int)(virtualScreen.Left + virtualScreen.Width / 2),
            (int)(virtualScreen.Top + virtualScreen.Height / 2));

        var screenTop = virtualScreen.Y / screenScale;
        var screenBottom = (virtualScreen.Y + virtualScreen.Height) / screenScale;
        var screenLeft = virtualScreen.X / screenScale;
        var screenRight = (virtualScreen.X + virtualScreen.Width) / screenScale;

        var toolbarWidth = ActualWidth > 0 ? ActualWidth : Width;
        var toolbarHeight = ActualHeight > 0 ? ActualHeight : Height;

        Left = dipLeft + (dipWidth - toolbarWidth) / 2;
        Left = Math.Max(screenLeft + minOutsideMargin, Math.Min(Left, screenRight - toolbarWidth - minOutsideMargin));

        var spaceAbove = dipTop - screenTop;
        var spaceBelow = screenBottom - dipBottom;
        var requiredSpace = toolbarHeight + toolbarMargin;

        if (spaceAbove >= requiredSpace && spaceAbove >= spaceBelow)
        {
            Top = dipTop - toolbarHeight - toolbarMargin;
        }
        else
        {
            Top = dipBottom + toolbarMargin;
        }

        if (Top + toolbarHeight > dipTop - minOutsideMargin && Top < dipBottom + minOutsideMargin)
        {
            Top = spaceBelow >= spaceAbove
                ? dipBottom + toolbarMargin
                : dipTop - toolbarHeight - toolbarMargin;
        }

        Top = Math.Max(screenTop + minOutsideMargin, Math.Min(Top, screenBottom - toolbarHeight - minOutsideMargin));
    }

    private void UpdateTimerDisplay()
    {
        if (_getElapsed is null)
        {
            return;
        }

        var elapsed = _getElapsed();
        TimerText.Text = elapsed.ToString(elapsed.Hours > 0 ? @"h\:mm\:ss" : @"m\:ss");
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e) =>
        PauseToggleRequested?.Invoke(this, EventArgs.Empty);

    private void SaveButton_Click(object sender, RoutedEventArgs e) =>
        SaveRequested?.Invoke(this, EventArgs.Empty);

    private static void ApplyNoActivate(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var style = GetWindowLong(hwnd, GwlExstyle);
        SetWindowLong(hwnd, GwlExstyle, style | WsExToolWindow | WsExNoActivate);
    }

    private const int GwlExstyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
