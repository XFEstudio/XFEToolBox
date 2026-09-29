# VideoStudio editor regression validation

## Verified release

2026-09-24: **96 checks passed**, exit code 0, including source compilation, production package validation and extracted-package compilation for VideoStudio **1.4.0**.

- Artifacts: `bin/Debug/net10.0-windows10.0.17763.0/test-artifacts/20260924-232559/`.
- Package: `xfestudio.video-studio-1.4.0.xfetool`, 89,719 bytes.
- SHA-256: `5161C2DF4D1AFF9AB43758867B70B261873278DD7A4790A7F7C4732F3A90F178`.
- Previous 1.3.0 package is retained; `LegacyExporter.cs` freezes that release's implementation for comparisons, not production use.

Uses actual workspace sources from `%LOCALAPPDATA%\XFEToolBox\CrossVersion\EditorWorkspaces\VideoStudio`, WPF toolbox resources, FFmpeg, and FFprobe. User videos and settings are not modified. Synthetic fixtures, exports, PCM samples and rendered screenshots are kept in a timestamped `bin/Debug/net10.0-windows10.0.17763.0/test-artifacts/` directory.

## Run

Requires Windows, .NET 10, and the existing video tool's `ToolData/xfestudio.video-studio/settings.json` containing working FFmpeg / FFprobe paths. This full validation run explicitly asserts NVIDIA hardware use and therefore requires a working NVENC GPU. Production automatic mode also probes QSV/AMF and works CPU-only; those hardware vendors were not independently validated on other machines.

```powershell
dotnet run --project tests/VideoStudio.Validation/VideoStudio.Validation.csproj -- --skip-host
dotnet run --project tests/VideoStudio.Validation/VideoStudio.Validation.csproj -- --check-package
dotnet run --project tests/VideoStudio.Validation/VideoStudio.Validation.csproj -- --benchmark-only
```

Without `--skip-host`, the tool is compiled through production `ToolProjectRunService.BuildAsync`. `--check-package` additionally uses the production package validator, compares package source bytes and compiles the extracted project.

## Coverage

- Actual probe/import, eight source thumbnails, waveform, dispatcher responsiveness, completed progress.
- Split, boundary rejection, exact fixed-frame-rate stepping, fractional frame-rate/non-drop timecode, undo/redo, trimmed/reordered sequence-to-source mapping and playback hand-off.
- Ripple trim/delete, per-clip and track audio mute, volume, invalid trim, failed import preservation, zoom/fit, busy-state guards.
- Both edge handles on both tracks, start/end extension limits, one-frame minimum, frame snapping, frozen drag scale while the timeline ripples, zoomed tail drag, cancellation and redo preservation, forty updates creating only one undo entry.
- Actual NVENC encoding, optimized CPU path, one-pass multi-clip output, non-mutating contiguous-clip coalescing, and smart-copy compressed audio/video SHA-256 identity.
- Forced FFmpeg GPU failures before encoding and after a completed batch; complete CPU restart avoids mixed H.264 headers. Unrecoverable failure retains existing destination bytes.
- Bounded multi-batch export, exact frame counts across batch joins, 29.97fps non-keyframe cuts, single-frame output, muted/gain-adjusted audio.
- Actual H.264/AAC MP4 output and full decode, expected duration, single-frame and silent-source output.
- Extracted source/output pixel comparison to verify actual cut intervals; decoded audio RMS to verify muted versus 50% gain segments.
- Source overwrite/bounds rejection, cancellation triggered by actual encoding progress, preservation of an existing destination, temporary-directory cleanup, unchanged source hash.
- Real WPF page/viewmodel, visible still-frame image, common proportional scale for timeline drawing and hit-testing, no error-level binding messages, empty state after split, safe close during active tasks, full/minimum-size/editor/subtitle/environment renders.

The generated media is synthetic. Drag tests invoke the same gesture methods called by the WPF pointer handlers on an instantiated, laid-out page; they do not claim an OS-level manual mouse/file-picker session. Arbitrary MediaElement codecs or long continuous hardware playback were not exhaustively tested. FFmpeg frame extraction/export is exercised end-to-end. Whisper recognition/download code is preserved, not re-downloaded/re-run for this change. No network download or source-media deletion is performed by this suite.

`results.json` records each passed assertion and success state. Native decoder `libpng` interlace warnings may appear while WPF renders; assertions also check that images load and binding errors are absent.

## Reproducible performance comparison

`--benchmark-only` creates one 1920x1080 / 30fps / 24-second H.264 source with a sine audio track, then exports the same six reordered/noncontiguous four-second clips (including one 60%-gain clip and one muted clip). The timed export includes probe, staging, encoder selection, encoding, muxing and cleanup, not fixture creation or verification. All exports are fully decoded and duration/dimensions/muted audio are checked. The two passes reverse ordering to reduce cache/order effects. CPU and GPU comparisons disable smart copy.

Measured on Ryzen 9 9950X3D, RTX 5090 (driver 32.0.16.1692), FFmpeg 9.0.1. Raw output: `bin/Debug/net10.0-windows10.0.17763.0/test-artifacts/20260924-232117/benchmark.json`.

| Path | Round 1 | Round 2 | Mean | Speedup | Output bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| Frozen 1.3.0, x264 medium / 4 threads, per-clip intermediates | 8.3703 s | 7.8796 s | 8.1249 s | 1.00x | 22,414,326 |
| 1.4.0 optimized CPU, x264 fast / CRF 20 | 4.2046 s | 4.1795 s | 4.1920 s | 1.94x | 23,952,379 |
| 1.4.0 automatic NVENC, p4 / CQ 20 | 3.3939 s | 2.9569 s | 3.1754 s | 2.56x | 40,288,951 |

Same resolution/frame rate and edit semantics, **not** identical encoding/compression quality or output size. CQ and CRF values are not equivalent across encoders; GPU output is larger in this fixture. Measurements are local synthetic results, not a universal speed guarantee. The benchmark checks are separate from the 96-check release suite.
