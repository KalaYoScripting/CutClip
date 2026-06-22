using System.Windows;
using CutClip.Interop;

namespace CutClip.Views;

public partial class CountdownOverlay : Window
{
    public CountdownOverlay()
    {
        InitializeComponent();
    }

    public void PositionAt(Rect physicalRegion)
    {
        var scale = DpiHelper.GetScaleForPoint(
            (int)(physicalRegion.Left + physicalRegion.Width / 2),
            (int)(physicalRegion.Top + physicalRegion.Height / 2));

        Left = physicalRegion.X / scale + (physicalRegion.Width / scale - Width) / 2;
        Top = physicalRegion.Y / scale + (physicalRegion.Height / scale - Height) / 2;
    }

    public void SetCount(int count)
    {
        CountdownText.Text = count.ToString();
    }
}
