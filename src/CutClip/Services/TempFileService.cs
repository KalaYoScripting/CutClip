namespace CutClip.Services;

public static class TempFileService
{
    private static readonly HashSet<string> TrackedFiles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Lock = new();

    static TempFileService()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupAll();
    }

    public static void Track(string path)
    {
        lock (Lock)
        {
            TrackedFiles.Add(path);
        }
    }

    public static void Untrack(string path)
    {
        lock (Lock)
        {
            TrackedFiles.Remove(path);
        }
    }

    public static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
        finally
        {
            Untrack(path);
        }
    }

    public static void CleanupAll()
    {
        string[] files;
        lock (Lock)
        {
            files = TrackedFiles.ToArray();
            TrackedFiles.Clear();
        }

        foreach (var file in files)
        {
            DeleteIfExists(file);
        }

        try
        {
            foreach (var file in Directory.GetFiles(Path.GetTempPath(), "CutClip_*"))
            {
                try { File.Delete(file); } catch { /* ignore */ }
            }
        }
        catch
        {
            // ignore
        }
    }
}
