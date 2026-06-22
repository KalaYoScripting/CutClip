using System.Windows;

namespace CutClip.Models;

public sealed class AppState
{
    public static AppState Instance { get; } = new();

    private AppState() { }

    public bool IsRecording { get; set; }
    public Rect SelectedRegion { get; set; }
    public DateTime StartTime { get; set; }
    public string OutputFilePath { get; set; } = string.Empty;
    public int Fps { get; set; } = 30;
    public bool RecordSystemAudio { get; set; }
    public bool RecordMicrophone { get; set; }
    public int CountdownSeconds { get; set; } = 0;
    public string OutputFormat { get; set; } = "mp4";
    public uint HotkeyVirtualKey { get; set; } = 0x78; // F9
    public uint HotkeyModifiers { get; set; }
    public bool CopyRecordingToClipboard { get; set; }
    public bool DisableNotifications { get; set; }

    public HotkeyBinding Hotkey => new()
    {
        VirtualKey = HotkeyVirtualKey,
        Modifiers = HotkeyModifiers
    };
}
