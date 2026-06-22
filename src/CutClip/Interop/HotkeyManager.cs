using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CutClip.Interop;

public sealed class HotkeyManager : IDisposable
{
    public const int HotkeyId = 0xCC01;

    private readonly HwndSource _hwndSource;
    private bool _registered;
    private bool _disposed;

    public event EventHandler? HotkeyPressed;

    public HotkeyManager(Window window)
    {
        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle();
        _hwndSource = HwndSource.FromHwnd(helper.Handle)
            ?? throw new InvalidOperationException("Failed to create HwndSource for hotkey registration.");
        _hwndSource.AddHook(WndProc);
    }

    public bool Register(uint virtualKey, uint modifiers = 0)
    {
        Unregister();

        _registered = RegisterHotKey(_hwndSource.Handle, HotkeyId, modifiers, virtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(_hwndSource.Handle, HotkeyId);
            _registered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;

        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unregister();
        _hwndSource.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
