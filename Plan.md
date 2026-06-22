# 🖥️ Screen Region Recorder — Development Plan

## 1. Application Overview

A lightweight Windows desktop utility that lets users select a specific region of their screen, record it, and automatically save the output as a video file to the system's default Videos folder (`%USERPROFILE%\Videos`). The app targets developers, content creators, teachers, and anyone who needs quick, no-fuss screen captures without the bloat of full-screen recorders like OBS. **Core value proposition:** The fastest way to record any screen region on Windows with zero configuration.

- **Application type:** Desktop App (Windows)

## 2. Goals & Success Metrics

| Goal | Success Metric |
|---|---|
| Launch and be ready to record in under 3 seconds | App startup time ≤ 3s on mid-range hardware |
| Minimal resource usage during recording | CPU usage ≤ 15%, RAM ≤ 80 MB during active recording |
| Reliable output saved to Windows Videos folder | 100% of recordings saved correctly with no corruption |
| Intuitive region selection with no learning curve | User can complete first recording in < 60 seconds with no instructions |
| Small install footprint | Installer < 50 MB, installed size < 100 MB |

## 3. Feature Breakdown

### MVP (Must-Have)

| Feature | Description | Complexity |
|---|---|---|
| Region Selection Tool | Click-and-drag overlay to define the recording area | Medium |
| Screen Recording Engine | Capture selected region at configurable FPS (default 30) | High |
| Auto-Save to Videos Folder | Automatically resolve `%USERPROFILE%\Videos` and save MP4 | Low |
| System Tray Icon | Minimize to tray; start/stop recording from tray or hotkey | Low |
| Start/Stop Hotkey | Global hotkey (e.g., `F9`) to start and stop recording | Low |
| Recording Timer Indicator | Small floating overlay showing elapsed recording time | Low |

### V2 (Should-Have)

| Feature | Description | Complexity |
|---|---|---|
| Audio Recording Toggle | Optionally capture system audio or microphone alongside video | Medium |
| Custom Output Format | Allow choosing between MP4, GIF, or WebM | Medium |
| Countdown Before Record | 3-second countdown before capture begins | Low |
| Custom Hotkey Config | Let users remap the start/stop hotkey | Low |
| Recent Recordings List | Quick-access list of last 5 recordings with "Open" button | Low |

### Future (Nice-to-Have)

| Feature | Description | Complexity |
|---|---|---|
| Annotation Overlay | Draw arrows/highlights on screen while recording | High |
| Auto-Upload to Cloud | Optional upload to Dropbox or Google Drive after saving | Medium |
| Scheduled Recording | Record a region at a set time or for a set duration | Medium |
| Cursor Highlight | Visually highlight mouse cursor during recording | Low |

## 4. System Architecture

```
┌─────────────────────────────────────────────┐
│             Windows Desktop App              │
│                                              │
│  ┌──────────────┐    ┌─────────────────────┐│
│  │  UI Layer    │    │  Recording Engine   ││
│  │  (Overlay /  │───▶│  (Screen Capture +  ││
│  │  Tray Icon)  │    │   FFmpeg Pipeline)  ││
│  └──────────────┘    └────────┬────────────┘│
│                               │              │
│                    ┌──────────▼────────────┐ │
│                    │  File Output Handler  │ │
│                    │  (%USERPROFILE%\Videos│ │
│                    │   filename.mp4)       │ │
│                    └───────────────────────┘ │
└─────────────────────────────────────────────┘
```

**Components:**
- **UI Layer:** Transparent fullscreen overlay for region selection + system tray icon + floating timer
- **Recording Engine:** Uses Windows Graphics Capture API or GDI BitBlt loop, piped into FFmpeg for encoding
- **File Output Handler:** Resolves the Videos folder path via `SHGetKnownFolderPath` (Windows API) and writes the final MP4

**Data Flow:** User draws region → coordinates passed to engine → raw frames captured in loop → piped to FFmpeg → encoded MP4 written to disk.

**No network, no database, no sync required** — fully offline.

## 5. Tech Stack Recommendation

| Layer | Choice | Why |
|---|---|---|
| **Language** | C# (.NET 8) | Native Windows integration, excellent WinAPI access, great performance |
| **UI Framework** | WPF (Windows Presentation Foundation) | Perfect for transparent overlays, tray icons, and lightweight windows |
| **Screen Capture** | Windows.Graphics.Capture API | Official Windows 10/11 API; hardware-accelerated, no dirty hacks |
| **Video Encoding** | FFmpeg (via `FFMpegCore` NuGet) | Industry-standard encoding; produces clean MP4/H.264 output |
| **Global Hotkeys** | Native Win32 `RegisterHotKey` P/Invoke | Lightweight, no third-party dependency needed |
| **Output Path** | `Environment.GetFolderPath(SpecialFolder.MyVideos)` | Correctly resolves Windows Videos folder for any user |
| **Packaging** | MSIX or single-file `.exe` (self-contained) | Easy distribution; no install required for portable version |
| **CI/CD** | GitHub Actions | Free, simple pipeline for build + release artifacts |

> **Alternative:** If you prefer Python for faster prototyping, use `mss` for capture + `opencv-python` + `ffmpeg-python` — but the C# stack will be significantly more performant and feel more "native."

## 6. Data Models

No database is needed. The app uses in-memory state and the filesystem only.

**AppState (in-memory singleton)**
| Field | Type | Description |
|---|---|---|
| `IsRecording` | `bool` | Whether a recording session is active |
| `SelectedRegion` | `Rect` | X, Y, Width, Height of capture area |
| `StartTime` | `DateTime` | When the current recording started |
| `OutputFilePath` | `string` | Full resolved path to the output file |
| `Fps` | `int` | Target frames per second (default: 30) |

**RecordingMetadata (saved as sidecar `.json` optionally in V2)**
| Field | Type | Description |
|---|---|---|
| `FileName` | `string` | Output file name |
| `Duration` | `TimeSpan` | Total recording length |
| `Region` | `Rect` | Captured screen region |
| `CreatedAt` | `DateTime` | Timestamp of recording |

## 7. Security & Permissions

- **No authentication needed** — single-user local desktop app.
- **Windows Permissions:** The app must request the Screen Capture permission on first run (Windows 10/11 prompts automatically via the Graphics Capture API).
- **File System Access:** Only writes to `%USERPROFILE%\Videos` — no elevated (admin) privileges required.
- **No network calls** in MVP — zero attack surface from network.
- **Code Signing (V2):** Sign the `.exe` with a certificate to prevent Windows SmartScreen warnings on distribution.
- **Temp Files:** Raw frame buffers written to `%TEMP%` during recording must be cleaned up on app exit or crash.

## 8. Development Roadmap

### Phase 1 — MVP (Estimated: 2–3 weeks)
**Definition of Done:** User can select a region, record it, and find the MP4 in their Videos folder.

- [ ] Project scaffold: WPF app + tray icon shell
- [ ] Fullscreen transparent overlay with click-drag region selector
- [ ] Windows.Graphics.Capture integration
- [ ] FFmpeg pipe for MP4 encoding
- [ ] Auto-save to Videos folder with timestamped filename (e.g., `Recording_2026-06-22_17-21.mp4`)
- [ ] Global hotkey (F9) to start/stop
- [ ] Floating timer overlay during recording

**Critical Path:** Region selector → Capture engine → FFmpeg encoding (these three must be done in sequence)

### Phase 2 — Polish & Audio (Estimated: 1–2 weeks)
- [ ] Audio capture (system + mic toggle)
- [ ] Settings panel (FPS, format, hotkey)
- [ ] Countdown overlay
- [ ] Recent recordings quick-list

### Phase 3 — Distribution (Estimated: 3–5 days)
- [ ] GitHub Actions build pipeline
- [ ] Single-file self-contained `.exe` packaging
- [ ] Code signing setup
- [ ] README + basic documentation

## 9. Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Windows Graphics Capture API requires Windows 10 v1903+ | Low | Medium | Add version check on startup; fallback to GDI BitBlt for older Windows |
| FFmpeg encoding lag causes dropped frames at high FPS | Medium | Medium | Buffer frames in a concurrent queue; encode asynchronously on a background thread |
| App crashes mid-recording, leaving corrupt file | Low | High | Write to a temp file first; move to Videos folder only on clean stop |
| SmartScreen blocks unsigned `.exe` on distribution | High | Low | Document workaround for users; prioritize code signing in Phase 3 |
| Region selection UX is confusing on multi-monitor setups | Medium | Medium | Span the overlay across all monitors; show monitor boundaries as guides |

## 10. Launch Checklist

**Pre-Launch Testing**
- [ ] Test on Windows 10 (1903+) and Windows 11
- [ ] Test on single-monitor and dual-monitor setups
- [ ] Test recordings of 30s, 5min, and 30min to validate file integrity
- [ ] Verify Videos folder path resolves correctly for non-English Windows installs
- [ ] Confirm temp files are cleaned up after normal exit and after a crash

**Deployment**
- [ ] Build self-contained single `.exe` (no .NET runtime install required for end user)
- [ ] Host release on GitHub Releases with version tag
- [ ] Write a one-page README with a GIF demo

**Post-Launch Monitoring**
- [ ] Add opt-in crash reporting (e.g., Sentry free tier) to capture unhandled exceptions
- [ ] GitHub Issues enabled for user bug reports
- [ ] Monitor file size of typical recordings to guide compression defaults
