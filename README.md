# CutClip

**CutClip** is a lightweight Windows desktop utility for quick screen-region recording. Select any area of your screen, record it, and get a timestamped video saved automatically to your Videos folder.

## Features

### MVP
- **Region selection** — click-and-drag overlay across all monitors
- **Screen recording** — Windows Graphics Capture API with H.264 MP4 encoding (via FFmpeg)
- **Auto-save** — files saved to `%USERPROFILE%\Videos\CutClip_yyyy-MM-dd_HH-mm-ss.mp4`
- **System tray** — runs quietly in the background
- **Global hotkey** — press **F9** to start/stop (configurable in Settings)
- **Recording timer** — floating overlay shows elapsed time

### Settings (Phase 2)
- FPS: 24 / 30 / 60
- Output format: MP4, WebM, GIF
- Countdown before recording (0, 3, or 5 seconds)
- System audio and microphone toggles
- Custom hotkey (F8 / F9 / F10)
- Recent recordings list with Open button
- JSON sidecar metadata per recording

## Requirements

- **Windows 10 version 1903+** or Windows 11
- **[FFmpeg](https://ffmpeg.org/download.html)** on your PATH (must include `ffmpeg.exe`)
- **.NET 8 Runtime** (not required for the self-contained published build)

## Quick Start

### Run from source

```powershell
dotnet restore CutClip.sln
dotnet run --project src/CutClip/CutClip.csproj
```

### Publish portable executable

```powershell
dotnet publish src/CutClip/CutClip.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o ./publish
```

The output is `./publish/CutClip.exe`.

## Usage

1. Launch CutClip — it appears in the system tray.
2. Click **Select Region & Record** (or press **F9**).
3. Drag a rectangle over the area you want to capture.
4. Recording starts automatically (optional countdown if configured).
5. Press **F9** again or choose **Stop Recording** from the tray menu.
6. Find your clip in the **Videos** folder.

## Project Structure

```
CutClip/
├── src/CutClip/
│   ├── Models/          AppState, RecordingMetadata
│   ├── Views/           Overlays, Settings, Recent recordings
│   ├── Services/        Capture, encoding, output paths
│   └── Interop/         Global hotkey registration
└── .github/workflows/   CI build and release
```

## Code Signing

Unsigned builds may trigger Windows SmartScreen on first run. To sign releases in CI, set these GitHub secrets:

- `WINDOWS_SIGNING_CERT` — base64-encoded `.pfx` certificate
- `WINDOWS_SIGNING_PASSWORD` — certificate password

## License

MIT
