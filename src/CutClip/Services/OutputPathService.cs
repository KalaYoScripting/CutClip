namespace CutClip.Services;

public static class OutputPathService
{
    public static string GetVideosFolder()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    }

    public static string GenerateOutputPath(string format = "mp4")
    {
        var videosFolder = GetVideosFolder();
        Directory.CreateDirectory(videosFolder);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var extension = format.TrimStart('.').ToLowerInvariant();
        return Path.Combine(videosFolder, $"CutClip_{timestamp}.{extension}");
    }

    public static string GenerateTempPath(string extension = "mp4")
    {
        return Path.Combine(Path.GetTempPath(), $"CutClip_{Guid.NewGuid():N}.{extension}");
    }
}
