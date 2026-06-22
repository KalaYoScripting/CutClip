using System.Diagnostics;
using System.Windows;
using CutClip.Models;
using CutClip.Services;

namespace CutClip.Views;

public partial class RecentRecordingsWindow : Window
{
    public RecentRecordingsWindow()
    {
        InitializeComponent();
        RecordingsList.ItemsSource = RecentRecordingsService.GetRecent()
            .Select(r => new
            {
                r.FileName,
                Duration = r.Duration.ToString(@"mm\:ss"),
                CreatedAt = r.CreatedAt.ToString("g"),
                r.FullPath
            })
            .ToList();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (RecordingsList.SelectedItem is null)
        {
            return;
        }

        var path = RecordingsList.SelectedItem.GetType().GetProperty("FullPath")?.GetValue(RecordingsList.SelectedItem) as string;
        if (path is not null && File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
