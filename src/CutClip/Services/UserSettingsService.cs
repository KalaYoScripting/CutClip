using System.Text;
using CutClip.Models;

namespace CutClip.Services;

public static class UserSettingsService
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CutClip");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    public static void Load(AppState state)
    {
        if (!File.Exists(SettingsPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var settings = System.Text.Json.JsonSerializer.Deserialize<UserSettings>(json);
            if (settings is null)
            {
                return;
            }

            state.Fps = settings.Fps is 24 or 30 or 60 ? settings.Fps : 30;
            state.OutputFormat = settings.OutputFormat is "mp4" or "webm" or "gif" ? settings.OutputFormat : "mp4";
            state.CountdownSeconds = settings.CountdownSeconds is 0 or 3 or 5 ? settings.CountdownSeconds : 0;
            state.RecordSystemAudio = settings.RecordSystemAudio;
            state.RecordMicrophone = settings.RecordMicrophone;
            state.HotkeyVirtualKey = settings.HotkeyVirtualKey;
            state.HotkeyModifiers = settings.HotkeyModifiers;
            state.CopyRecordingToClipboard = settings.CopyRecordingToClipboard;
            state.DisableNotifications = settings.DisableNotifications;
        }
        catch
        {
            // Ignore corrupt settings and use defaults.
        }
    }

    public static void Save(AppState state)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);

            var settings = new UserSettings
            {
                Fps = state.Fps,
                OutputFormat = state.OutputFormat,
                CountdownSeconds = state.CountdownSeconds,
                RecordSystemAudio = state.RecordSystemAudio,
                RecordMicrophone = state.RecordMicrophone,
                HotkeyVirtualKey = state.HotkeyVirtualKey,
                HotkeyModifiers = state.HotkeyModifiers,
                CopyRecordingToClipboard = state.CopyRecordingToClipboard,
                DisableNotifications = state.DisableNotifications
            };

            var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Non-fatal if settings cannot be written.
        }
    }

    private sealed class UserSettings
    {
        public int Fps { get; set; } = 30;
        public string OutputFormat { get; set; } = "mp4";
        public int CountdownSeconds { get; set; }
        public bool RecordSystemAudio { get; set; }
        public bool RecordMicrophone { get; set; }
        public uint HotkeyVirtualKey { get; set; } = 0x78;
        public uint HotkeyModifiers { get; set; }
        public bool CopyRecordingToClipboard { get; set; }
        public bool DisableNotifications { get; set; }
    }
}
