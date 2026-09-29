# FormatConverter validation

Runs the actual FormatConverter source, Magick.NET native codecs, FFmpeg/FFprobe, and a real WPF view with the toolbox theme. No user media is modified or used. Input fixtures are generated in a per-run test-artifacts directory.

## Latest verified result

2026-09-13: **135 checks passed**, exit code 0, using `--check-package --register`. Both the workspace and the extracted package compiled successfully through the toolbox production builder; all package file bytes matched the workspace.

Artifacts: `bin/Debug/net10.0-windows10.0.17763.0/test-artifacts/run-20260913-025643/`.

Package: `xfestudio.format-converter-1.0.0.xfetool` (39,022 bytes).

SHA-256: `DA15E9C1061A582DB8B61041CE2C5CD55B42B0ED6753D2A1145D09137026DD46`.

## Run

Prerequisites: Windows, .NET 10 SDK, NuGet access/cache, the tool workspace at `%LOCALAPPDATA%\XFEToolBox\CrossVersion\EditorWorkspaces\FormatConverter`, and a working FFmpeg/FFprobe pair discoverable by `EngineSetup` (PATH, previously configured environment, or VideoStudio tool runtime).

```powershell
dotnet run --project tests/FormatConverter.Validation/FormatConverter.Validation.csproj
```

Optional switches:

- `--skip-host`: codec/guard/WPF checks only, without the toolbox production compilation step.
- `--check-package`: also extract the version-matching `.xfetool` from the workspace sibling `Packages` directory using the production package loader, byte-compare against the workspace, and compile the extracted package.
- `--register`: remember the project in the toolbox's editor project list. Omit this for a read-only registration check.

Results and rendered WPF screenshots are saved under `bin/Debug/net10.0-windows10.0.17763.0/test-artifacts/run-<timestamp>/`. The test process exits nonzero on any failed assertion; `results.json` records successful assertions and overall status. A deterministic code-drawn icon is emitted to the tool's `Assets/icon.png` before production compilation.

## Coverage

- Encode/decode every advertised image writer (35 with Magick.NET 14.17.1 on the tested machine), including ICO's seven sizes and DDS DXT5 header. Reverse conversions for ICO/DDS/JPEG/WebP/TIFF/TGA/AVIF/PSD, animated frame preservation and rejection of unintended frame loss.
- All available 12 audio and 12 video/GIF output presets: actual conversion, FFprobe stream checks and complete FFmpeg decode to a null sink. Video-to-audio and resize presets.
- Self-generated MP3 and FLAC NCM containers, including cover padding and payloads exceeding the streaming buffer size: bit-exact extraction, extraction without FFmpeg, real WAV transcode, invalid/truncated header rejection.
- Cancellation of a running FFmpeg process, timeout, active conversion cancellation without partial publication, same-name output safety, unchanged source hash and temporary directory cleanup.
- Corrupt images, SVG rasterization/external-reference rejection, executable/playlist extension exclusion and renamed concat demuxer rejection.
- Real ZIP extraction, traversal rejection, HTTP download via controlled fixture transport, matching and mismatching SHA-256.
- Real ViewModel conversion command, UI dispatcher heartbeat, batch continuation after a bad file, safe close during active work, audio/NCM tab, WPF error-level binding tracing, full/minimum-size page rendering.
- Production toolbox build, and optionally package extraction/byte identity/extracted-project compilation.

## Tested scope and limits

The local run uses Magick.NET-Q16-AnyCPU 14.17.1 and the existing Gyan FFmpeg 9.0.1 Essentials installation. No network download or modification of that existing FFmpeg installation is performed by these tests.

NCM fixtures are synthetic protocol samples, not user-provided service downloads. This does not prove compatibility with every NCM variant. No exhaustive fuzzing, every input codec variant, HEIC input sample, large real-world project, full HDR precision preservation, or manual click/drag/file-picker test is claimed. WPF is instantiated/rendered and its actual command is invoked programmatically; package compilation is distinct from a full manually operated runtime session.

The installer tests exercise stream download/checksum/extraction with a mock HTTP transport; they do not assert live Gyan availability or a complete live download installation. The one-click configuration code additionally verifies downloaded executables before remembering them.
