using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CutClip.Interop;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace CutClip.Views;

public partial class RegionSelectorOverlay : Window
{
    private Point _startPoint;
    private bool _isDragging;
    private Rect _physicalOrigin;
    private double _windowScale = 1.0;

    public Rect? SelectedRegion { get; private set; }

    public RegionSelectorOverlay()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SpanAllMonitors();
    }

    private void SpanAllMonitors()
    {
        _physicalOrigin = MonitorHelper.GetVirtualScreenBounds();
        _windowScale = DpiHelper.GetScaleForPoint(
            (int)_physicalOrigin.Left,
            (int)_physicalOrigin.Top);

        var dipBounds = DpiHelper.PhysicalToDip(_physicalOrigin, _windowScale);
        Left = dipBounds.Left;
        Top = dipBounds.Top;
        Width = dipBounds.Width;
        Height = dipBounds.Height;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(RootCanvas);
        _isDragging = true;
        SelectionRect.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionRect, _startPoint.X);
        Canvas.SetTop(SelectionRect, _startPoint.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        CaptureMouse();
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        var current = e.GetPosition(RootCanvas);
        var x = Math.Min(_startPoint.X, current.X);
        var y = Math.Min(_startPoint.Y, current.Y);
        var w = Math.Abs(current.X - _startPoint.X);
        var h = Math.Abs(current.Y - _startPoint.Y);

        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        ReleaseMouseCapture();

        if (SelectionRect.Width >= 5 && SelectionRect.Height >= 5)
        {
            ConfirmSelection();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SelectedRegion = null;
            DialogResult = false;
            Close();
        }
        else if (e.Key == Key.Enter && SelectionRect.Width >= 5 && SelectionRect.Height >= 5)
        {
            ConfirmSelection();
        }
    }

    private void ConfirmSelection()
    {
        var canvasX = Canvas.GetLeft(SelectionRect);
        var canvasY = Canvas.GetTop(SelectionRect);

        var topLeft = RootCanvas.PointToScreen(new Point(canvasX, canvasY));
        var bottomRight = RootCanvas.PointToScreen(
            new Point(canvasX + SelectionRect.Width, canvasY + SelectionRect.Height));

        SelectedRegion = new Rect(
            topLeft.X,
            topLeft.Y,
            bottomRight.X - topLeft.X,
            bottomRight.Y - topLeft.Y);

        DialogResult = true;
        Close();
    }
}
