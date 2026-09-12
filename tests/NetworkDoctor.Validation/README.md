# NetworkDoctor validation

Windows / .NET 10 WPF regression harness. Links actual tool sources from `%LOCALAPPDATA%\XFEToolBox\CrossVersion\EditorWorkspaces\NetworkDoctor`; override `ToolWorkspace` via MSBuild if needed. Uses the real host theme, package validator, and compiler.

```powershell
dotnet run --project tests/NetworkDoctor.Validation/NetworkDoctor.Validation.csproj -- --live
dotnet run --project tests/NetworkDoctor.Validation/NetworkDoctor.Validation.csproj -- --check-package --register
```

- Normal run: classification/repair guard tests, simulated mutation runner, loopback DNS (UDP and truncated-response TCP fallback), HTTP and TCP fixtures, real read-only child processes, output encoding/size/cancellation/timeout tests, PowerShell AST parsing of production scripts (no repair execution), WPF binding and responsive layout screenshots, host compilation.
- `--live`: runs the production ViewModel scan on the current machine, checks UI dispatcher responsiveness, streamed results and opt-in repair choices. A guard rejects any command marked `Mutates`. No actual network configuration is modified.
- `--check-package`: validates and extracts `Packages/xfestudio.network-doctor-1.0.0.xfetool`, byte-compares every file with the workspace, compiles the extracted package using the production host.
- `--register`: adds the engineering directory to the tool editor's recent project list.

Artifacts are under `bin/Debug/net10.0-windows10.0.17763.0/test-artifacts`: `results.json`, three WPF screenshots, simulated repair journals, and optionally `live-report.txt` (contains private network configuration; do not commit).

Tests create child PowerShell processes that only print text, exit, parse strings, or sleep. Timeout/cancellation stops only those children. Repairs use a fake executor; no flush, renew, adapter restart, registry change or Winsock/WinHTTP reset is executed. WPF windows used for off-screen rendering do not appear in the taskbar.

Live administrator/UAC interactions and real disruptive repairs on a disposable network are not automated by this harness. Command success is not asserted as proof of restored connectivity.
