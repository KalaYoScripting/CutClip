using System.Windows.Input;

namespace CutClip.Models;

public sealed class HotkeyBinding
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    public uint VirtualKey { get; init; } = 0x78; // F9
    public uint Modifiers { get; init; }

    public static HotkeyBinding Default { get; } = new();

    public string DisplayName => Format(Modifiers, VirtualKey);

    public static string Format(uint modifiers, uint virtualKey)
    {
        var parts = new List<string>();

        if ((modifiers & ModControl) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((modifiers & ModAlt) != 0)
        {
            parts.Add("Alt");
        }

        if ((modifiers & ModShift) != 0)
        {
            parts.Add("Shift");
        }

        if ((modifiers & ModWin) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(FormatVirtualKey(virtualKey));
        return string.Join(" + ", parts);
    }

    public static uint ModifiersFromKeyboard()
    {
        uint mods = 0;
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
        {
            mods |= ModControl;
        }

        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
        {
            mods |= ModAlt;
        }

        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
        {
            mods |= ModShift;
        }

        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
        {
            mods |= ModWin;
        }

        return mods;
    }

    public static bool IsModifierKey(Key key) =>
        key is Key.LeftAlt or Key.RightAlt
            or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.System;

    private static string FormatVirtualKey(uint virtualKey) =>
        virtualKey switch
        {
            0x70 => "F1",
            0x71 => "F2",
            0x72 => "F3",
            0x73 => "F4",
            0x74 => "F5",
            0x75 => "F6",
            0x76 => "F7",
            0x77 => "F8",
            0x78 => "F9",
            0x79 => "F10",
            0x7A => "F11",
            0x7B => "F12",
            0x1B => "Esc",
            0x20 => "Space",
            0x0D => "Enter",
            >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
            >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
            _ => $"0x{virtualKey:X}"
        };
}
