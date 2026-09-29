# VideoStudio 1.5.0 — multi-track regression validation

Verified 2026-09-30: **106 checks passed**, exit code 0. Includes actual FFmpeg processing, WPF playback/layout/gestures, native maximized window geometry, production workspace compilation, package validation and extracted-package compilation.

- Results and PNG/media artifacts: `bin/Debug/net10.0-windows10.0.17763.0/test-artifacts/20260930-034854/`.
- Package: `%LOCALAPPDATA%/XFEToolBox/CrossVersion/EditorWorkspaces/Packages/xfestudio.video-studio-1.5.0.xfetool`.
- Size: 104,700 bytes.
- SHA-256: `678E4F587843D9A820ABAEA6C85A949F75EB2B5CB31681A4025ABCB8855A948A`.
- Toolbox Release build: 0 warnings, 0 errors. Window chrome changes require the newly compiled host, not just the video package.

## Run

Requires Windows, .NET 10, and working FFmpeg / FFprobe paths in the existing tool's `ToolData/xfestudio.video-studio/settings.json`. Full validation asserts actual hardware encoding on this machine (NVIDIA NVENC). Production also probes Intel QSV / AMD AMF and supports CPU-only fallback; this does not claim independent validation on every GPU vendor.

```powershell
dotnet run --project tests/VideoStudio.Validation/VideoStudio.Validation.csproj -- --skip-host
dotnet run --project tests/VideoStudio.Validation/VideoStudio.Validation.csproj -- --check-package
dotnet run --project tests/VideoStudio.Validation/VideoStudio.Validation.csproj -- --host-only --check-package
```

Source links point at `%LOCALAPPDATA%/XFEToolBox/CrossVersion/EditorWorkspaces/VideoStudio`. No user media or saved settings are changed. Tests generate their own files under a timestamped test-artifacts directory and use only owned test windows. Playback is muted in the test view; sound is validated through decoded PCM and frequency measurements. Production build-only checks do not launch or replace an installed toolbox.

## Coverage

- Real video/audio/image imports, thumbnails, waveform, dispatcher responsiveness, duplicate/failed import preservation, MP3 attached-cover classification.
- Independent original audio and video; additional video/audio tracks, cross-track move/type rejection, separate selected/video/audio splits, locked-track guards.
- Head/tail frame snapping, minimum one-frame length, cancel, unchanged other tracks, many pointer updates as one undo step, undo/redo.
- Per-clip name/time/track, gain to 200%, mute, fade-in/out, image length, scale, position, opacity; invalid values rejected atomically.
- Project save/open round-trip preserving IDs, tracks, clips, source-time subtitles and settings; subtitle dirty-state tracking; malformed project preserves current edits; source overwrite rejection.
- Actual composite preview generation/cache/invalidation, WPF MediaElement playback advancing the playhead, and transport pause.
- CPU and hardware export, whole-output decode, exact expected dimensions/duration, layer pixel checks against paused previews, overlay end/black-gap behavior, scaling.
- Actual mixed 440Hz/660Hz audio, per-clip 50%/150% gain, fade-out, clip/global mute, audio-only and image-only exports.
- Existing single-source smart-copy fast path retained; forced unavailable GPU encoder successfully retries on CPU.
- Active-encoding cancellation preserves existing output and removes private staging.
- 70 one-frame image clips rendered through bounded batches, pixel comparison at a batch boundary; too many simultaneous inputs fail explicitly.
- Real WPF menu commands, type-specific inspector visibility, compatible-track selection and readable names, pointer gesture methods, viewport resize/fit, minimum layout, zero error-level binding traces.
- Tool-window maximize/restore: no margins or radii when maximized, no resize-grip rendering/input, exact Win32 bounds matching monitor work area, restored chrome, nonresizable-window grip behavior.
- All synthetic source hashes unchanged, production workspace and extracted-package compilation, package bytes identical to workspace.

## Scope and historical tests

WPF gesture checks invoke the methods used by real pointer handlers on an instantiated and laid-out view. They are not a manual OS mouse/file-picker acceptance session. Short local composite playback is exercised; arbitrary formats, long playback, every display/DPI topology and GPU/driver are not exhaustively tested. Whisper download/recognition is unchanged and not re-downloaded or rerun.

`Program.cs` and `AccelerationTests.cs` are preserved 1.4 historical tests and excluded from current compilation because they exercise the old linked/ripple timeline semantics. `LegacyExporter.cs` preserves the earlier exporter for reference. The active suite is the `MultiTrack*.cs` files. Old 1.4 speed benchmarks (8.12s old CPU / 4.19s optimized CPU / 3.18s NVENC on 24s 1080p footage) are historical only, not a speed claim for multi-layer compositing. The previous 1.4.0 package is retained.
