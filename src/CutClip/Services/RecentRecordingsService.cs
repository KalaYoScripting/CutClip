using CutClip.Models;

namespace CutClip.Services;

public static class RecentRecordingsService
{
    private const int MaxRecent = 5;
    private static readonly List<RecordingMetadata> Recent = new();

    public static IReadOnlyList<RecordingMetadata> GetRecent() => Recent.AsReadOnly();

    public static void Add(RecordingMetadata metadata)
    {
        Recent.Insert(0, metadata);
        while (Recent.Count > MaxRecent)
        {
            Recent.RemoveAt(Recent.Count - 1);
        }
    }
}
