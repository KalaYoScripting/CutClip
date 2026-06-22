using System.Windows;
using CutClip.Models;
using CutClip.Services;

namespace CutClip;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        UserSettingsService.Load(AppState.Instance);
        _ = Task.Run(FFmpegProbe.WarmUp);
    }
}
