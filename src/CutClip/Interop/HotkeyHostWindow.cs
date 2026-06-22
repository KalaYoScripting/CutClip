using System.Windows;

namespace CutClip.Interop;

/// <summary>
/// Invisible window that stays shown so its HWND receives WM_HOTKEY messages.
/// </summary>
public sealed class HotkeyHostWindow : Window
{
    public HotkeyHostWindow()
    {
        Title = "CutClip Hotkey Host";
        Width = 1;
        Height = 1;
        Left = -32000;
        Top = -32000;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        Opacity = 0;
        ResizeMode = ResizeMode.NoResize;
    }
}
