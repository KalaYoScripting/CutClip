using System.Runtime.InteropServices;
using System.Windows;

namespace CutClip.Interop;

public static class DpiHelper
{
    private const int MdtEffectiveDpi = 0;

    public static uint GetDpiForPoint(int x, int y)
    {
        var monitor = MonitorHelper.GetMonitorHandleForPoint(x, y);
        GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _);
        return dpiX;
    }

    public static double GetScaleForPoint(int x, int y) => GetDpiForPoint(x, y) / 96.0;

    public static Rect PhysicalToDip(Rect physical, double scale) =>
        new(physical.X / scale, physical.Y / scale, physical.Width / scale, physical.Height / scale);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);
}
