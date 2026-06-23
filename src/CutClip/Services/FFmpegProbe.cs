using System.Diagnostics;

namespace CutClip.Services;

public static class FFmpegProbe
{
    private static bool? _wasapiSupported;
    private static IReadOnlyList<DshowAudioDevice>? _dshowAudioDevices;
    private static readonly object Gate = new();
    private static bool _initialized;

    public static void WarmUp()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            _ = IsWasapiSupported();
            _ = GetDshowAudioDeviceDetails();
            _initialized = true;
        }
    }

    public static bool IsWasapiSupported()
    {
        lock (Gate)
        {
            if (_wasapiSupported is null)
            {
                var output = RunFFmpeg("-hide_banner -formats");
                _wasapiSupported = output.Contains(" wasapi", StringComparison.OrdinalIgnoreCase);
            }

            return _wasapiSupported.Value;
        }
    }

    public static IReadOnlyList<DshowAudioDevice> GetDshowAudioDeviceDetails()
    {
        lock (Gate)
        {
            if (_dshowAudioDevices is not null)
            {
                return _dshowAudioDevices;
            }

            var output = RunFFmpeg("-hide_banner -list_devices true -f dshow -i dummy");
            var lines = output.Split('\n');
            var devices = new List<DshowAudioDevice>();

            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                var audioIndex = trimmed.IndexOf("\" (audio)", StringComparison.OrdinalIgnoreCase);
                if (audioIndex <= 0)
                {
                    continue;
                }

                var start = trimmed.IndexOf('"');
                if (start < 0)
                {
                    continue;
                }

                var name = trimmed[(start + 1)..audioIndex];
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                string? alternativeName = null;
                if (i + 1 < lines.Length)
                {
                    var next = lines[i + 1].Trim();
                    const string altPrefix = "Alternative name \"";
                    if (next.StartsWith(altPrefix, StringComparison.OrdinalIgnoreCase)
                        && next.EndsWith('"'))
                    {
                        alternativeName = next[altPrefix.Length..^1];
                    }
                }

                devices.Add(new DshowAudioDevice
                {
                    Name = name,
                    AlternativeName = alternativeName
                });
            }

            _dshowAudioDevices = devices;
            return devices;
        }
    }

    private static string RunFFmpeg(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = FfmpegLocator.ResolveExecutable(),
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return string.Empty;
            }

            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();

            if (!process.WaitForExit(15000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignore
                }

                return string.Empty;
            }

            return stderrTask.GetAwaiter().GetResult() + stdoutTask.GetAwaiter().GetResult();
        }
        catch
        {
            return string.Empty;
        }
    }
}
