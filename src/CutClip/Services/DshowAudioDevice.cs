namespace CutClip.Services;

public sealed class DshowAudioDevice
{
    public required string Name { get; init; }

    public string? AlternativeName { get; init; }

    public string FFmpegInput =>
        AlternativeName is not null
            ? $"audio={AlternativeName}"
            : $"audio=\"{Name.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
