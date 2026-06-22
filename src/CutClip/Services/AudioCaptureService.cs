namespace CutClip.Services;

/// <summary>
/// Builds FFmpeg audio input arguments using WASAPI when available, otherwise DirectShow.
/// System audio falls back to native WASAPI loopback when FFmpeg lacks WASAPI (Stereo Mix is unreliable).
/// </summary>
public static class AudioCaptureService
{
    public static bool IsAudioEnabled(bool systemAudio, bool microphone) =>
        systemAudio || microphone;

    public static bool TryBuild(
        bool systemAudio,
        bool microphone,
        out string inputArgs,
        out string mapArgs,
        out bool useNativeSystemLoopback,
        out string? error)
    {
        inputArgs = string.Empty;
        mapArgs = string.Empty;
        useNativeSystemLoopback = false;
        error = null;

        if (!systemAudio && !microphone)
        {
            return true;
        }

        if (FFmpegProbe.IsWasapiSupported())
        {
            BuildWasapi(systemAudio, microphone, out inputArgs, out mapArgs);
            return true;
        }

        if (systemAudio)
        {
            useNativeSystemLoopback = true;

            if (microphone)
            {
                var micResult = TryBuildDshow(systemAudio: false, microphone: true, out inputArgs, out mapArgs, out error);
                if (!micResult)
                {
                    return false;
                }

                mapArgs = "-filter_complex \"[1:a][2:a]amix=inputs=2:duration=longest[aout]\" -map 0:v -map \"[aout]\" -c:a aac -b:a 192k -shortest ";
            }
            else
            {
                mapArgs = "-map 0:v -map 1:a -c:a aac -b:a 192k -shortest ";
            }

            return true;
        }

        return TryBuildDshow(systemAudio, microphone, out inputArgs, out mapArgs, out error);
    }

    private static void BuildWasapi(bool systemAudio, bool microphone, out string inputArgs, out string mapArgs)
    {
        inputArgs = string.Empty;

        if (systemAudio)
        {
            inputArgs += "-thread_queue_size 512 -f wasapi -loopback 1 -i default ";
        }

        if (microphone)
        {
            inputArgs += "-thread_queue_size 512 -f wasapi -i default ";
        }

        mapArgs = BuildMapArgs(systemAudio, microphone);
    }

    private static bool TryBuildDshow(
        bool systemAudio,
        bool microphone,
        out string inputArgs,
        out string mapArgs,
        out string? error)
    {
        inputArgs = string.Empty;
        mapArgs = string.Empty;
        error = null;

        var devices = FFmpegProbe.GetDshowAudioDeviceDetails();
        if (devices.Count == 0)
        {
            error = "No audio capture devices found. Install FFmpeg with WASAPI support (gyan.dev builds) for system audio.";
            return false;
        }

        DshowAudioDevice? systemDevice = null;
        DshowAudioDevice? micDevice = null;

        if (systemAudio)
        {
            systemDevice = devices.FirstOrDefault(d => IsLoopbackDeviceName(d.Name));
            if (systemDevice is null)
            {
                error = "System audio requires FFmpeg with WASAPI support, or enable Stereo Mix in Windows sound settings.";
                return false;
            }
        }

        if (microphone)
        {
            micDevice = devices.FirstOrDefault(d => d.Name.Contains("microphone", StringComparison.OrdinalIgnoreCase))
                ?? devices.FirstOrDefault(d => !IsLoopbackDeviceName(d.Name));

            if (micDevice is null)
            {
                error = "No microphone device found.";
                return false;
            }
        }

        if (systemDevice is not null)
        {
            inputArgs += BuildDshowInput(systemDevice);
        }

        if (micDevice is not null)
        {
            inputArgs += BuildDshowInput(micDevice);
        }

        mapArgs = BuildMapArgs(systemAudio, microphone);
        return true;
    }

    private static string BuildDshowInput(DshowAudioDevice device) =>
        $"-thread_queue_size 512 -f dshow -audio_buffer_size 50 -sample_rate 44100 -channels 2 -i {device.FFmpegInput} ";

    private static string BuildMapArgs(bool systemAudio, bool microphone)
    {
        if (!systemAudio && !microphone)
        {
            return string.Empty;
        }

        if (systemAudio && microphone)
        {
            return "-filter_complex \"[1:a][2:a]amix=inputs=2:duration=longest[aout]\" -map 0:v -map \"[aout]\" -c:a aac -b:a 192k -async 1 -shortest ";
        }

        return "-map 0:v -map 1:a -c:a aac -b:a 192k -async 1 -shortest ";
    }

    private static bool IsLoopbackDeviceName(string device) =>
        device.Contains("Stereo Mix", StringComparison.OrdinalIgnoreCase)
        || device.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
        || device.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase)
        || device.Contains("virtual-audio-capturer", StringComparison.OrdinalIgnoreCase)
        || device.Contains("What U Hear", StringComparison.OrdinalIgnoreCase);
}
