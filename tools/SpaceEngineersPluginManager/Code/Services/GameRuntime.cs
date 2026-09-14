using System.Diagnostics;
using System.IO;

namespace XFEToolBox.Tools.SpaceEngineers;

public sealed record RunningGame(int Id, string ExecutablePath, DateTime StartTimeUtc);

public interface IGameProcessHost
{
    IReadOnlyList<RunningGame> Find(IReadOnlyList<string> executablePaths);
    bool IsAlive(RunningGame game);
    bool RequestClose(RunningGame game);
    void Start(ProcessStartInfo startInfo);
}

public sealed class WindowsGameProcessHost : IGameProcessHost
{
    public IReadOnlyList<RunningGame> Find(IReadOnlyList<string> executablePaths)
    {
        var result = new List<RunningGame>();
        foreach (var name in executablePaths.Select(Path.GetFileNameWithoutExtension).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        string? path = process.MainModule?.FileName;
                        if (path != null && executablePaths.Any(expected => SamePath(expected, path)))
                            result.Add(new(process.Id, path, process.StartTime.ToUniversalTime()));
                    }
                    catch (InvalidOperationException) { /* Process exited during enumeration. */ }
                    catch (System.ComponentModel.Win32Exception ex)
                    { throw new IOException("无法确认游戏进程身份；请以与游戏相同的权限运行此工具。", ex); }
                }
            }
        }
        return result.DistinctBy(game => game.Id).ToArray();
    }

    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    private static Process? Open(RunningGame game)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(game.Id);
            if (!process.HasExited && process.StartTime.ToUniversalTime() == game.StartTimeUtc &&
                process.MainModule?.FileName is string path && SamePath(path, game.ExecutablePath)) return process;
            process.Dispose(); return null;
        }
        catch (ArgumentException) { process?.Dispose(); return null; }
        catch (InvalidOperationException) { process?.Dispose(); return null; }
    }
    public bool IsAlive(RunningGame game) { using var process = Open(game); return process != null; }
    public bool RequestClose(RunningGame game) { using var process = Open(game); return process == null || process.CloseMainWindow(); }
    public void Start(ProcessStartInfo startInfo) { using var process = Process.Start(startInfo) ?? throw new IOException("Windows 未能启动游戏。 "); }
}

public sealed class GameRuntime(IGameProcessHost? host = null)
{
    private readonly IGameProcessHost host = host ?? new WindowsGameProcessHost();
    public static IReadOnlyList<string> Executables(ManagerSettings settings)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.Bin64Path))
        {
            paths.Add(Path.Combine(Path.GetFullPath(settings.Bin64Path), "SpaceEngineers.exe"));
            // A game started before switching to XFE must also finish before another starts.
            paths.Add(Path.Combine(Path.GetFullPath(settings.Bin64Path), "SpaceEngineersLauncher.exe"));
        }
        if (!string.IsNullOrWhiteSpace(settings.LauncherPath))
        {
            string launcher = Path.GetFullPath(settings.LauncherPath);
            paths.Add(launcher);
        }
        paths.AddRange(settings.KnownLauncherPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath));
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public IReadOnlyList<RunningGame> Find(ManagerSettings settings) => host.Find(Executables(settings));

    public async Task CloseAsync(ManagerSettings settings, Action<string>? progress = null, CancellationToken token = default)
    {
        var games = Find(settings);
        foreach (var game in games)
        {
            token.ThrowIfCancellationRequested();
            if (!host.RequestClose(game)) throw new IOException("游戏没有可关闭的主窗口，请在游戏中保存并正常退出后重试。工具不会强制结束游戏。");
        }
        if (games.Count == 0) return;
        progress?.Invoke("已请求正常退出；如游戏询问是否保存，请在游戏中完成。正在等待进程退出…");
        var deadline = DateTime.UtcNow.AddSeconds(Math.Clamp(settings.CloseTimeoutSeconds, 10, 600));
        while (games.Any(host.IsAlive) || Find(settings).Count != 0)
        {
            token.ThrowIfCancellationRequested();
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("游戏尚未退出，已取消本次操作；插件配置保持原样。请保存并退出游戏后重试。");
            await Task.Delay(250, token);
        }
    }

    public static ProcessStartInfo BuildStartInfo(ManagerSettings settings, bool vanilla, bool requireProfile = true)
    {
        string bin = Path.GetFullPath(settings.Bin64Path);
        string executable = vanilla ? Path.Combine(bin, "SpaceEngineers.exe") : Path.GetFullPath(settings.LauncherPath);
        if (!File.Exists(Path.Combine(bin, "SpaceEngineers.exe"))) throw new FileNotFoundException("请选择包含 SpaceEngineers.exe 的 Bin64 目录。");
        if (!File.Exists(executable)) throw new FileNotFoundException("未找到启动器，请先构建 XFE 加载器。", executable);
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = vanilla ? bin : Path.GetDirectoryName(executable)! };
        if (!vanilla)
        {
            if (!File.Exists(settings.BridgePath)) throw new FileNotFoundException("请先构建 XFE 插件加载组件。", settings.BridgePath);
            if (requireProfile && !File.Exists(settings.ProfilePath)) throw new FileNotFoundException("请先应用或保存插件配置。", settings.ProfilePath);
            info.ArgumentList.Add("--game-bin64"); info.ArgumentList.Add(bin);
            info.ArgumentList.Add("--bridge"); info.ArgumentList.Add(Path.GetFullPath(settings.BridgePath));
            info.ArgumentList.Add("--profile"); info.ArgumentList.Add(Path.GetFullPath(settings.ProfilePath));
            info.ArgumentList.Add("--log"); info.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(settings.LauncherPath)!, "xfe-loader.log"));
            info.ArgumentList.Add("--");
        }
        info.Environment["SteamAppId"] = "244850";
        info.Environment["SteamGameId"] = "244850";
        foreach (string arg in settings.Arguments ?? [])
        {
            if (arg.IndexOf('\0') >= 0) throw new ArgumentException("启动参数不能含 NUL 字符。");
            if (arg.Equals("-plugin", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("--game-bin64", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("--bridge", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("--profile", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("插件加载由 XFE 管理，不要在额外参数中重复覆盖。");
            info.ArgumentList.Add(arg);
        }
        return info;
    }

    public void Launch(ManagerSettings settings, bool vanilla)
    {
        if (Find(settings).Count != 0) throw new InvalidOperationException("游戏已经运行，请使用重启或先正常退出。");
        host.Start(BuildStartInfo(settings, vanilla));
    }
}
