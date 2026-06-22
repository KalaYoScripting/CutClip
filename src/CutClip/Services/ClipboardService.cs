using System.Diagnostics;

namespace CutClip.Services;

public static class ClipboardService
{
    public static void CopyFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        System.Windows.Clipboard.SetFileDropList([filePath]);
    }
}

public static class ExplorerHelper
{
    public static void SelectFileInExplorer(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{filePath}\"",
            UseShellExecute = true
        });
    }
}
