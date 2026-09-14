using System.IO;
using System.Text.Json;
using XFEToolBox.Core.Tools;

namespace XFEToolBox.Tools.SpaceEngineers;

public sealed class ManagerService : IDisposable
{
    private readonly string dataDirectory;
    private readonly GameRuntime game;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly PluginProfileStore profiles = new();
    private ProfileSnapshot? current;
    private Dictionary<string, string>? watchHashes;
    private string? pendingWatchHash;
    private DateTime watchChangedAt;
    private bool disposed;
    private DateTime launchPendingUntil;
    private string logPath = "";
    private long logOffset;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public ManagerSettings Settings { get; }
    public event Action<string>? Progress;

    public ManagerService(string? dataDirectory = null, IGameProcessHost? processHost = null)
    {
        this.dataDirectory = dataDirectory ?? ToolDataStore.DataDirectory;
        game = new(processHost);
        string file = Path.Combine(this.dataDirectory, "settings.json");
        Settings = File.Exists(file) ? JsonSerializer.Deserialize<ManagerSettings>(File.ReadAllText(file), Json) ?? new() : new();
        NormalizeSettings();
    }
    private void NormalizeSettings()
    {
        Settings.Arguments ??= []; Settings.KnownLauncherPaths ??= [];
        if (string.IsNullOrWhiteSpace(Settings.ProfilePath)) Settings.ProfilePath = Path.Combine(this.dataDirectory, "profiles", "default.json");
        GameDiscovery.FillDefaults(Settings);
    }
    private void Report(string message) => Progress?.Invoke(message);
    private async Task Locked(Func<Task> action, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(token);
        try { await action(); } finally { gate.Release(); }
    }
    public async Task SaveSettingsAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); NormalizeSettings();
        Directory.CreateDirectory(dataDirectory);
        string path = Path.Combine(dataDirectory, "settings.json"), temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(Settings, Json), token); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public ManagerState ReadProcessState()
    {
        var running = game.Find(Settings);
        return new()
        {
            IsRunning = running.Count != 0,
            ProcessStatus = running.Count == 0 ? "游戏未运行" : "游戏运行中 · PID " + string.Join(", ", running.Select(p => p.Id)),
            LoaderStatus = File.Exists(Settings.LauncherPath) && File.Exists(Settings.BridgePath) ? "XFE Loader 已构建" : "请先构建 XFE 加载器"
        };
    }
    public async Task<ManagerState> RefreshAsync(CancellationToken token = default)
    {
        ManagerState? state = null;
        await Locked(() =>
        {
            NormalizeSettings();
            current = profiles.Load(Settings.ProfilePath); var process = ReadProcessState();
            state = new() { IsRunning = process.IsRunning, ProcessStatus = process.ProcessStatus, LoaderStatus = process.LoaderStatus,
                Plugins = current.Plugins.Select(Row).ToArray() };
            watchHashes = null; pendingWatchHash = null; return Task.CompletedTask;
        }, token);
        return state!;
    }
    private static PluginRow Row(StoredPlugin plugin) => new()
    {
        Id = plugin.Id, Name = plugin.Name, Location = plugin.AssemblyPath, SourcePath = plugin.SourcePath,
        IsEnabled = plugin.Enabled, Description = File.Exists(plugin.AssemblyPath) ? "已导入 · 应用后由 XFE 加载" : "文件缺失，请重新导入"
    };
    private static StoredPlugin Stored(PluginRow row) => new(row.Id, row.Name, row.Location, row.SourcePath, row.IsEnabled);
    private ProfileSnapshot Expected()
    {
        if (current == null || !Path.GetFullPath(current.FilePath).Equals(Path.GetFullPath(Settings.ProfilePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("配置路径已改变，请先刷新插件列表。");
        return current;
    }
    public async Task<PluginRow> ImportLocalPluginAsync(string source, bool development, CancellationToken token = default)
    {
        StoredPlugin? imported = null;
        await Locked(async () => imported = await Task.Run(() => PluginFiles.Import(source, Path.Combine(dataDirectory, "plugins"), token: token), token), token);
        Report("插件和依赖已复制到独立版本目录，应用更改后生效。"); return Row(imported!);
    }
    public Task ApplyAsync(IEnumerable<PluginRow> rows, bool restart, CancellationToken token = default)
    {
        var desired = rows.Select(Stored).ToArray();
        return Locked(async () =>
        {
            var expected = Expected(); profiles.ValidateSave(expected, desired);
            bool running = game.Find(Settings).Count != 0;
            if (running)
            {
                if (!restart) throw new InvalidOperationException("游戏运行中：请先保存并退出，或启用应用后自动重启。");
                Preflight(true);
                await game.CloseAsync(Settings, Report, token);
            }
            token.ThrowIfCancellationRequested();
            current = profiles.Save(expected, desired); watchHashes = null; pendingWatchHash = null;
            Report("XFE 插件配置已应用；旧配置与旧 DLL 版本保留，可在备份页恢复。");
            if (running && restart) { launchPendingUntil = default; Start(false); }
        }, token);
    }
    private void Start(bool vanilla)
    {
        if (DateTime.UtcNow < launchPendingUntil) throw new InvalidOperationException("已发出启动请求，请等待游戏窗口出现。");
        if (!vanilla)
        {
            if (!File.Exists(Settings.ProfilePath)) current = profiles.Save(profiles.Load(Settings.ProfilePath), []);
            profiles.ValidateForLaunch(Settings.ProfilePath);
            XfeLoaderBuilder.ValidateInstallation(Settings.Bin64Path, Settings.LauncherPath, Settings.BridgePath);
        }
        game.Launch(Settings, vanilla); launchPendingUntil = DateTime.UtcNow.AddSeconds(15);
        Report("已发出游戏启动请求，正在等待游戏窗口；加载情况将在日志页显示。");
    }
    private void Preflight(bool willWriteProfile)
    {
        XfeLoaderBuilder.ValidateInstallation(Settings.Bin64Path, Settings.LauncherPath, Settings.BridgePath);
        _ = GameRuntime.BuildStartInfo(Settings, false, requireProfile: !willWriteProfile);
        if (!willWriteProfile) profiles.ValidateForLaunch(Settings.ProfilePath);
    }
    public Task LaunchAsync(bool vanilla, CancellationToken token = default) => Locked(async () => { await SaveSettingsAsync(token); Start(vanilla); }, token);
    public Task CloseGameAsync(CancellationToken token = default) => Locked(async () => { await game.CloseAsync(Settings, Report, token); launchPendingUntil = default; Report("游戏已正常退出。"); }, token);
    public Task RestartAsync(CancellationToken token = default) => Locked(async () => { Preflight(false); await game.CloseAsync(Settings, Report, token); launchPendingUntil = default; Start(false); }, token);
    public IReadOnlyList<string> GetBackups() => profiles.GetBackupPaths(Settings.ProfilePath);
    public Task RestoreAsync(string backupPath, bool restart, CancellationToken token = default) => Locked(async () =>
    {
        var expected = Expected(); profiles.ValidateRestore(expected, backupPath);
        bool running = game.Find(Settings).Count != 0;
        if (running)
        {
            if (!restart) throw new InvalidOperationException("请先退出游戏，或启用恢复后重启。");
            Preflight(true);
            await game.CloseAsync(Settings, Report, token);
        }
        token.ThrowIfCancellationRequested(); current = profiles.RestoreBackup(expected, backupPath); watchHashes = null;
        Report("配置已恢复，恢复前的版本也已备份。"); if (running && restart) { launchPendingUntil = default; Start(false); }
    }, token);
    public Task BuildLoaderAsync(CancellationToken token = default) => Locked(async () =>
    {
        var installation = await new XfeLoaderBuilder().BuildAsync(Settings.Bin64Path, Path.Combine(dataDirectory, "loader"), Report, token);
        if (!string.IsNullOrWhiteSpace(Settings.LauncherPath)) Settings.KnownLauncherPaths.Add(Settings.LauncherPath);
        Settings.KnownLauncherPaths = Settings.KnownLauncherPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Settings.LauncherPath = installation.LauncherPath; Settings.BridgePath = installation.BridgePath;
        await SaveSettingsAsync(token);
        Report("XFE 启动引导和插件桥已基于本机游戏编译，游戏文件未修改。");
    }, token);
    private void PumpLog()
    {
        if (string.IsNullOrWhiteSpace(Settings.LauncherPath)) return;
        string path = Path.Combine(Path.GetDirectoryName(Settings.LauncherPath)!, "xfe-loader.log");
        if (!File.Exists(path)) return;
        if (path != logPath) { logPath = path; logOffset = 0; }
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (input.Length < logOffset) logOffset = 0;
        input.Position = Math.Max(logOffset, input.Length - 65536);
        using var reader = new StreamReader(input);
        string text = reader.ReadToEnd(); logOffset = input.Position;
        if (text.Length != 0) Report(text.TrimEnd());
    }
    public async Task<bool> PollAsync(CancellationToken token = default)
    {
        if (disposed || !await gate.WaitAsync(0, token)) return false;
        bool committed = false;
        try
        {
            PumpLog();
            if (!Settings.RestartOnLocalChanges) { watchHashes = null; pendingWatchHash = null; return false; }
            if (current == null) return false;
            var enabled = current.Plugins.Where(plugin => plugin.Enabled && !string.IsNullOrWhiteSpace(plugin.SourcePath)).ToArray();
            var hashes = await Task.Run(() => enabled.ToDictionary(plugin => plugin.Id, plugin => PluginFiles.Fingerprint(plugin.SourcePath)), token);
            if (watchHashes == null) { watchHashes = hashes; return false; }
            string change = string.Join(";", hashes.Where(pair => !watchHashes.TryGetValue(pair.Key, out string? hash) || hash != pair.Value).OrderBy(pair => pair.Key).Select(pair => pair.Key + pair.Value));
            if (change.Length == 0) { pendingWatchHash = null; return false; }
            if (pendingWatchHash != change) { pendingWatchHash = change; watchChangedAt = DateTime.UtcNow; return false; }
            if ((DateTime.UtcNow - watchChangedAt).TotalSeconds < 3) return false;
            bool running = game.Find(Settings).Count != 0;
            if (running) Preflight(true);
            var desired = current.Plugins.ToList();
            for (int i = 0; i < desired.Count; i++)
            {
                var plugin = desired[i];
                if (hashes.TryGetValue(plugin.Id, out var hash) && (!watchHashes.TryGetValue(plugin.Id, out var old) || hash != old))
                    desired[i] = await Task.Run(() => PluginFiles.Import(plugin.SourcePath, Path.Combine(dataDirectory, "plugins"), plugin.Id, plugin.Name, token), token);
            }
            profiles.ValidateSave(current, desired);
            if (running) { Report("插件新版本已准备好，正在正常退出游戏以应用并重启…"); await game.CloseAsync(Settings, Report, token); }
            token.ThrowIfCancellationRequested(); current = profiles.Save(current, desired); watchHashes = hashes; pendingWatchHash = null;
            committed = true;
            Report("已同步新的 DLL 与依赖，旧版本仍可恢复。");
            if (running) { launchPendingUntil = default; Start(false); }
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Settings.RestartOnLocalChanges = false; Report("自动同步已暂停：" + ex.Message); return committed; }
        finally { gate.Release(); }
    }
    public void Dispose() { disposed = true; }
}
