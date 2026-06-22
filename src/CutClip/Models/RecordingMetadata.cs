using System.Text.Json.Serialization;
using System.Windows;

namespace CutClip.Models;

public sealed class RecordingMetadata
{
    public string FileName { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public Rect Region { get; set; }
    public DateTime CreatedAt { get; set; }
    public int Fps { get; set; }
    public string Format { get; set; } = "mp4";

    [JsonIgnore]
    public string FullPath { get; set; } = string.Empty;
}
