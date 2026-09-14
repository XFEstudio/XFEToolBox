using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using XFEToolBox.Client.Utilities;

namespace XFEToolBox.Tools.SpaceEngineers.ViewModels;

public sealed partial class LaunchArgument : ObservableObject
{
    [ObservableProperty] private string value = "";
}

public sealed record BackupItem(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

public sealed partial class MainPageViewModel : ObservableObject
{
    private readonly ManagerService service;
    private readonly Dispatcher dispatcher;
    private readonly bool pollingEnabled;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Queue<string> logLines = new();
    private CancellationTokenSource? operationCancellation;
    private Task? pollingTask;
    private bool initialized, shuttingDown;
    private string lastPollError = "";

    public ObservableCollection<PluginRow> Plugins { get; } = [];
    public ObservableCollection<LaunchArgument> Arguments { get; } = [];
    public ObservableCollection<BackupItem> Backups { get; } = [];
    public ICollectionView PluginsView { get; }
    public string[] PluginFilters { get; } = ["全部插件", "已启用", "已停用"];

    [ObservableProperty] private int selectedTab;
    [ObservableProperty] private int selectedPluginFilter;
    [ObservableProperty] private string filter = "";
    [ObservableProperty] private string bin64Path = "";
    [ObservableProperty] private string launcherPath = "";
    [ObservableProperty] private string profilePath = "";
    [ObservableProperty] private string closeTimeoutText = "120";
    [ObservableProperty] private bool autoRestartAfterApply;
    [ObservableProperty] private bool restartOnLocalChanges;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isPolling;
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool isDirty;
    [ObservableProperty] private PluginRow? selectedPlugin;
    [ObservableProperty] private BackupItem? selectedBackup;
    [ObservableProperty] private string processStatus = "正在读取游戏状态…";
    [ObservableProperty] private string loaderStatus = "请配置游戏与插件加载器。";
    [ObservableProperty] private string status = "准备读取插件配置。";
    [ObservableProperty] private string logText = "";

    public bool CanEdit => !IsBusy && !IsPolling && !shuttingDown;
    public bool CanLaunch => CanEdit && !IsRunning;
    public bool CanClose => CanEdit && IsRunning;
    public bool CanApply => CanEdit && IsDirty;
    public bool CanRemove => CanEdit && SelectedPlugin is not null;
    public bool CanRestore => CanEdit && SelectedBackup is not null;
    public bool HasSelectedPlugin => SelectedPlugin is not null;
    public bool ShowEmptyPlugins => PluginsView.IsEmpty;
    public string EmptyPluginsTitle => Plugins.Count == 0 ? "还没有插件" : "没有匹配的插件";
    public string EmptyPluginsHint => Plugins.Count == 0 ? "添加本地插件 DLL 或包含已编译插件的目录，然后应用更改。首次使用请在「启动与环境」构建 XFE 加载器。" : "试试其他关键词，或切换到「全部插件」。";
    public string PluginSummary => $"{Plugins.Count} 个插件 · {Plugins.Count(p => p.IsEnabled)} 个启用" + (IsDirty ? " · 有待应用更改" : "");
    public string ApplyHint => AutoRestartAfterApply ? "应用后自动正常重启运行中的游戏；请先保存存档。" : IsRunning ? "游戏运行中。请先正常退出，或在「启动与环境」启用应用后自动重启。" : "插件更改在下次启动游戏时生效。";
    public string BackupSummary => Backups.Count == 0 ? "当前配置还没有备份。首次应用已有配置时会自动创建。" : $"发现 {Backups.Count} 份配置备份。恢复后是否重启遵循自动重启设置。";

    public MainPageViewModel() : this(new ManagerService()) { }

    public MainPageViewModel(ManagerService service, bool pollingEnabled = true)
    {
        this.service = service;
        this.pollingEnabled = pollingEnabled;
        dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        PluginsView = CollectionViewSource.GetDefaultView(Plugins);
        PluginsView.Filter = MatchPlugin;
        service.Progress += OnProgress;
        ReadSettings();
    }

    public async Task InitializeAsync()
    {
        if (initialized || shuttingDown) return;
        initialized = true;
        await RunAsync("读取游戏与插件配置", async token => await RefreshCoreAsync(token));
        if (pollingEnabled && !shuttingDown) pollingTask = PollLoopAsync();
    }

    private bool MatchPlugin(object item)
    {
        if (item is not PluginRow plugin) return false;
        bool category = SelectedPluginFilter switch
        {
            1 => plugin.IsEnabled,
            2 => !plugin.IsEnabled,
            _ => true
        };
        return category && (string.IsNullOrWhiteSpace(Filter) || Contains(plugin.Name, Filter) || Contains(plugin.Id, Filter) || Contains(plugin.Location, Filter) || Contains(plugin.SourcePath, Filter));
    }

    private static bool Contains(string? text, string search) => text?.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) == true;
    partial void OnFilterChanged(string value) => RefreshPluginView();
    partial void OnSelectedPluginFilterChanged(int value) => RefreshPluginView();
    partial void OnSelectedPluginChanged(PluginRow? value) { OnPropertyChanged(nameof(HasSelectedPlugin)); RemovePluginCommand.NotifyCanExecuteChanged(); }
    partial void OnSelectedBackupChanged(BackupItem? value) => RestoreCommand.NotifyCanExecuteChanged();
    partial void OnAutoRestartAfterApplyChanged(bool value) => OnPropertyChanged(nameof(ApplyHint));
    partial void OnIsDirtyChanged(bool value) { OnPropertyChanged(nameof(PluginSummary)); ApplyCommand.NotifyCanExecuteChanged(); }
    partial void OnIsRunningChanged(bool value) { UpdateCommands(); OnPropertyChanged(nameof(ApplyHint)); }
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(CanEdit)); UpdateCommands(); }
    partial void OnIsPollingChanged(bool value) { OnPropertyChanged(nameof(CanEdit)); UpdateCommands(); }

    private void UpdateCommands()
    {
        foreach (var command in new IRelayCommand[]
        {
            RefreshCommand, DetectCommand, SaveSettingsCommand, ApplyCommand, LaunchCommand, LaunchVanillaCommand,
            CloseGameCommand, RestartCommand, BuildLoaderCommand, ImportDllCommand, ImportDirectoryCommand, RemovePluginCommand,
            AddArgumentCommand, RemoveArgumentCommand, BrowseGameCommand, BrowseProfileCommand,
            RestoreCommand, RefreshBackupsCommand, OpenGameDirectoryCommand, CancelCommand
        }) command.NotifyCanExecuteChanged();
    }

    private void RefreshPluginView()
    {
        PluginsView.Refresh();
        OnPropertyChanged(nameof(ShowEmptyPlugins)); OnPropertyChanged(nameof(EmptyPluginsTitle));
        OnPropertyChanged(nameof(EmptyPluginsHint)); OnPropertyChanged(nameof(PluginSummary));
    }

    private void OnPluginChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PluginRow.IsEnabled)) return;
        IsDirty = true;
        RefreshPluginView();
    }

    private void AddRow(PluginRow row)
    {
        row.PropertyChanged += OnPluginChanged;
        Plugins.Add(row);
    }

    private void ReadSettings()
    {
        var settings = service.Settings;
        Bin64Path = settings.Bin64Path; LauncherPath = settings.LauncherPath; ProfilePath = settings.ProfilePath;
        AutoRestartAfterApply = settings.AutoRestartAfterApply;
        RestartOnLocalChanges = settings.RestartOnLocalChanges;
        CloseTimeoutText = settings.CloseTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Arguments.Clear();
        foreach (string argument in settings.Arguments) Arguments.Add(new LaunchArgument { Value = argument });
    }

    private void WriteSettings()
    {
        if (!int.TryParse(CloseTimeoutText, out int seconds) || seconds is < 10 or > 600)
            throw new ArgumentException("正常退出等待时间请输入 10–600 秒的整数。");
        var settings = service.Settings;
        settings.Bin64Path = Bin64Path.Trim(); settings.ProfilePath = ProfilePath.Trim();
        settings.Arguments = Arguments.Select(a => a.Value).ToList();
        settings.AutoRestartAfterApply = AutoRestartAfterApply;
        settings.RestartOnLocalChanges = RestartOnLocalChanges;
        settings.CloseTimeoutSeconds = seconds;
    }

    private async Task RefreshCoreAsync(CancellationToken token, bool replacePlugins = true, bool readSettings = true)
    {
        var state = replacePlugins ? await service.RefreshAsync(token) : service.ReadProcessState();
        IsRunning = state.IsRunning; ProcessStatus = state.ProcessStatus; LoaderStatus = state.LoaderStatus;
        if (replacePlugins)
        {
            string? selectedId = SelectedPlugin?.Id;
            foreach (var row in Plugins) row.PropertyChanged -= OnPluginChanged;
            Plugins.Clear();
            foreach (var row in state.Plugins) AddRow(row);
            SelectedPlugin = Plugins.FirstOrDefault(p => p.Id == selectedId);
            IsDirty = false;
            RefreshPluginView();
        }
        if (readSettings) ReadSettings();
        RefreshBackups();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task RefreshAsync() => RunAsync("刷新插件清单", async token =>
    {
        if (IsDirty && !ConfirmDiscard()) return;
        WriteSettings();
        await RefreshCoreAsync(token);
    });

    private static bool ConfirmDiscard() => PopupHelper.ShowConfirmDialog("当前插件清单有尚未应用的更改。重新读取将放弃这些更改。", true, "重新读取", "保留更改") == MessageBoxResult.OK;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task DetectAsync() => RunAsync("自动检测游戏与加载器", async token =>
    {
        if (IsDirty && !ConfirmDiscard()) return;
        WriteSettings();
        service.Settings.Bin64Path = "";
        await RefreshCoreAsync(token);
    });

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task SaveSettingsAsync() => RunAsync("保存环境设置", async token =>
    {
        WriteSettings();
        await service.SaveSettingsAsync(token);
        await RefreshCoreAsync(token, replacePlugins: !IsDirty);
    });

    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task ApplyAsync() => RunAsync("应用插件配置", async token =>
    {
        WriteSettings();
        await service.SaveSettingsAsync(token);
        await service.ApplyAsync(Plugins.ToArray(), AutoRestartAfterApply, token);
        await RefreshCoreAsync(token);
    });

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private Task LaunchAsync() => LaunchCoreAsync(false);

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private Task LaunchVanillaAsync() => LaunchCoreAsync(true);

    private Task LaunchCoreAsync(bool vanilla) => RunAsync(vanilla ? "启动原版游戏" : "启动插件游戏", async token =>
    {
        WriteSettings();
        await service.SaveSettingsAsync(token);
        await service.LaunchAsync(vanilla, token);
        await RefreshCoreAsync(token, replacePlugins: !IsDirty);
    });

    [RelayCommand(CanExecute = nameof(CanClose))]
    private Task CloseGameAsync() => RunAsync("等待游戏正常退出", async token =>
    {
        WriteSettings();
        await service.CloseGameAsync(token);
        await RefreshCoreAsync(token, replacePlugins: !IsDirty);
    });

    [RelayCommand(CanExecute = nameof(CanClose))]
    private Task RestartAsync() => RunAsync("正常重启游戏", async token =>
    {
        WriteSettings();
        await service.SaveSettingsAsync(token);
        await service.RestartAsync(token);
        await RefreshCoreAsync(token, replacePlugins: !IsDirty);
    });

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task BuildLoaderAsync() => RunAsync("构建或更新 XFE 加载器", async token =>
    {
        if (IsDirty && !ConfirmDiscard()) return;
        WriteSettings();
        await service.BuildLoaderAsync(token);
        await RefreshCoreAsync(token);
    });

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task ImportDllAsync()
    {
        var dialog = new OpenFileDialog { Title = "选择本地插件 DLL", Filter = "插件程序集 (*.dll)|*.dll", Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        await ImportAsync(dialog.FileName, false);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task ImportDirectoryAsync()
    {
        var dialog = new OpenFolderDialog { Title = "选择包含已编译插件 DLL 的目录" };
        if (dialog.ShowDialog() != true) return;
        await ImportAsync(dialog.FolderName, true);
    }

    private Task ImportAsync(string source, bool development) => RunAsync("添加本地插件", async token =>
    {
        WriteSettings();
        var row = await service.ImportLocalPluginAsync(source, development, token);
        var existing = Plugins.FirstOrDefault(p => p.Id.Equals(row.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is null) AddRow(row); else existing.IsEnabled = true;
        SelectedPlugin = existing ?? row;
        IsDirty = true;
        RefreshPluginView();
    });

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void RemovePlugin()
    {
        if (SelectedPlugin is not { } row) return;
        row.PropertyChanged -= OnPluginChanged;
        Plugins.Remove(row);
        SelectedPlugin = null;
        IsDirty = true;
        RefreshPluginView();
        AppendLog($"已从待应用清单移除 {row.Name}，保留插件文件。");
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void BrowseGame()
    {
        var dialog = new OpenFolderDialog { Title = "选择 Space Engineers 的 Bin64 文件夹", InitialDirectory = Directory.Exists(Bin64Path) ? Bin64Path : "" };
        if (dialog.ShowDialog() == true) Bin64Path = dialog.FolderName;
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void BrowseProfile()
    {
        var dialog = new OpenFileDialog { Title = "选择 XFE 插件配置", Filter = "XFE 插件配置 (*.json)|*.json" };
        if (dialog.ShowDialog() == true) ProfilePath = dialog.FileName;
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddArgument() => Arguments.Add(new LaunchArgument());

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void RemoveArgument(LaunchArgument? argument) { if (argument is not null) Arguments.Remove(argument); }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void OpenGameDirectory()
    {
        try
        {
            if (!Directory.Exists(Bin64Path)) throw new DirectoryNotFoundException("请先选择有效的游戏 Bin64 文件夹。");
            Process.Start(new ProcessStartInfo(Path.GetFullPath(Bin64Path)) { UseShellExecute = true });
        }
        catch (Exception e) { AppendLog("打开游戏目录失败：" + e.Message); }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void RefreshBackups()
    {
        try
        {
            string? selectedPath = SelectedBackup?.Path;
            Backups.Clear();
            foreach (string path in service.GetBackups()) Backups.Add(new BackupItem(path));
            SelectedBackup = Backups.FirstOrDefault(b => b.Path == selectedPath) ?? Backups.FirstOrDefault();
            OnPropertyChanged(nameof(BackupSummary));
        }
        catch (Exception e) { AppendLog("读取备份失败：" + e.Message); }
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private Task RestoreAsync() => RunAsync("恢复插件配置备份", async token =>
    {
        if (SelectedBackup is not { } backup) return;
        if (PopupHelper.ShowConfirmDialog($"将插件清单与加载版本恢复为：\n{backup.Name}\n\n当前配置会先备份。" + (IsDirty ? "未应用的清单更改将被放弃。" : ""), true, "恢复备份", "取消") != MessageBoxResult.OK) return;
        WriteSettings();
        await service.RestoreAsync(backup.Path, AutoRestartAfterApply, token);
        await RefreshCoreAsync(token);
    });

    [RelayCommand]
    private void ExportLog()
    {
        var dialog = new SaveFileDialog { Title = "导出运行日志", Filter = "文本文件 (*.txt)|*.txt", FileName = $"SE插件管理日志-{DateTime.Now:yyyyMMdd-HHmmss}.txt" };
        if (dialog.ShowDialog() != true) return;
        try { File.WriteAllText(dialog.FileName, LogText, new System.Text.UTF8Encoding(false)); AppendLog("日志已导出：" + dialog.FileName); }
        catch (Exception e) { AppendLog("导出日志失败：" + e.Message); }
    }

    [RelayCommand]
    private void ClearLog() { logLines.Clear(); LogText = ""; }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => operationCancellation?.Cancel();

    private async Task RunAsync(string title, Func<CancellationToken, Task> action)
    {
        if (!CanEdit) return;
        IsBusy = true;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operationCancellation = cancel;
        bool entered = false;
        try
        {
            await operationGate.WaitAsync(cancel.Token); entered = true;
            AppendLog(title + "…");
            await action(cancel.Token);
            AppendLog(title + "完成。");
        }
        catch (OperationCanceledException) { if (!shuttingDown) AppendLog(title + "已取消。已完成的步骤保留，请刷新确认当前状态。"); }
        catch (Exception e) { if (!shuttingDown) AppendLog(title + "失败：" + e.Message); }
        finally
        {
            if (entered) operationGate.Release();
            operationCancellation = null;
            IsBusy = false;
        }
    }

    private async Task PollLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
                await PollOnceAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    internal async Task PollOnceAsync(CancellationToken token = default)
    {
        if (IsBusy || shuttingDown || !await operationGate.WaitAsync(0, token)) return;
        bool monitoringWasEnabled = service.Settings.RestartOnLocalChanges;
        IsPolling = true;
        try
        {
            bool synchronized = !IsDirty && await service.PollAsync(token);
            if (synchronized)
            {
                // Our synchronization committed new managed DLL paths. Refresh those paths
                // before a later Apply can write the old versions back into the profile.
                await RefreshCoreAsync(token, readSettings: false);
            }
            else
            {
                // Ordinary polling must not replace the optimistic-edit snapshot.
                var state = service.ReadProcessState();
                IsRunning = state.IsRunning; ProcessStatus = state.ProcessStatus; LoaderStatus = state.LoaderStatus;
            }
            lastPollError = "";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            if (e.Message != lastPollError) { AppendLog("后台监视：" + e.Message); lastPollError = e.Message; }
        }
        finally
        {
            if (monitoringWasEnabled && !service.Settings.RestartOnLocalChanges) RestartOnLocalChanges = false;
            IsPolling = false;
            operationGate.Release();
        }
    }

    private void OnProgress(string message)
    {
        if (shuttingDown) return;
        if (dispatcher.CheckAccess()) AppendLog(message);
        else dispatcher.BeginInvoke(new Action(() => { if (!shuttingDown) AppendLog(message); }));
    }

    private void AppendLog(string message)
    {
        Status = message;
        logLines.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (logLines.Count > 400) logLines.Dequeue();
        LogText = string.Join(Environment.NewLine, logLines);
    }

    public async Task ShutdownAsync()
    {
        if (shuttingDown) return;
        shuttingDown = true;
        service.Progress -= OnProgress;
        await lifetime.CancelAsync();
        operationCancellation?.Cancel();
        if (pollingTask is not null) await pollingTask;
        await operationGate.WaitAsync();
        try { service.Dispose(); }
        finally { operationGate.Release(); }
        foreach (var row in Plugins) row.PropertyChanged -= OnPluginChanged;
        lifetime.Dispose();
    }
}
