using System.Windows;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Views.Windows;

namespace XFEToolBox.Client;

public partial class App : Application
{
    private SingleInstanceService? singleInstanceService;
    private TrayIconService? trayIconService;
    private GlobalHotkeyService? globalHotkeyService;
    private CommandPaletteWindow? commandPaletteWindow;
    private MainWindow? mainWindow;

    public App() => InitializeComponent();

    public bool IsExiting { get; private set; }

    public string GlobalHotkeyStatus => globalHotkeyService?.Status ?? "快捷键服务尚未初始化";

    public event EventHandler? GlobalHotkeyStatusChanged;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        singleInstanceService = new SingleInstanceService();
        if (!singleInstanceService.IsFirstInstance)
        {
            var command = e.Args.Contains("--palette", StringComparer.OrdinalIgnoreCase) ? "show-palette" : "show-main";
            _ = SingleInstanceService.SendAsync(command).GetAwaiter().GetResult();
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        mainWindow = new MainWindow();
        MainWindow = mainWindow;
        globalHotkeyService = new GlobalHotkeyService(mainWindow, () => ShowCommandPalette());
        globalHotkeyService.StatusChanged += (_, _) => GlobalHotkeyStatusChanged?.Invoke(this, EventArgs.Empty);
        if (SystemProfile.LauncherHotkeyEnabled)
            globalHotkeyService.Register(SystemProfile.LauncherHotkey);
        else
            globalHotkeyService.Disable();
        trayIconService = new TrayIconService(
            () => ShowMainWindow("home"),
            () => ShowCommandPalette(),
            RequestExit);
        singleInstanceService.StartListening(message => Dispatcher.BeginInvoke(() =>
        {
            if (message.Equals("show-palette", StringComparison.OrdinalIgnoreCase)) ShowCommandPalette();
            else ShowMainWindow("home");
        }));

        if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
            ShowMainWindow("home");
    }

    public void ShowMainWindow(string? pageTag = null)
    {
        if (mainWindow is null) return;
        if (!mainWindow.IsVisible) mainWindow.Show();
        if (mainWindow.WindowState == WindowState.Minimized) mainWindow.WindowState = WindowState.Normal;
        if (!string.IsNullOrWhiteSpace(pageTag))
            mainWindow.NavigateAndSelect(pageTag);
        mainWindow.Activate();
        mainWindow.Topmost = true;
        mainWindow.Topmost = false;
        mainWindow.Focus();
    }

    public void ShowCommandPalette(string? initialQuery = null)
    {
        commandPaletteWindow ??= new CommandPaletteWindow();
        commandPaletteWindow.ShowPalette(initialQuery);
    }

    public bool ConfigureGlobalHotkey(string gestureText)
    {
        if (!GlobalHotkeyService.TryNormalize(gestureText, out var normalized, out _)) return false;
        SystemProfile.LauncherHotkey = normalized;
        SystemProfile.SaveProfile();
        if (!SystemProfile.LauncherHotkeyEnabled)
        {
            globalHotkeyService?.Disable();
            return true;
        }
        return globalHotkeyService?.Register(normalized) == true;
    }

    public void ConfigureGlobalHotkeyEnabled(bool enabled)
    {
        SystemProfile.LauncherHotkeyEnabled = enabled;
        SystemProfile.SaveProfile();
        if (enabled)
            globalHotkeyService?.Register(SystemProfile.LauncherHotkey);
        else
            globalHotkeyService?.Disable();
    }

    public void HideMainWindowToTray()
    {
        mainWindow?.Hide();
        if (SystemProfile.TrayCloseHintShown) return;
        SystemProfile.TrayCloseHintShown = true;
        SystemProfile.SaveProfile();
        trayIconService?.ShowCloseHint();
    }

    public void RequestExit()
    {
        if (IsExiting) return;
        if (ActivityCenterService.ActiveCount > 0)
        {
            var result = PopupHelper.ShowConfirmDialog(
                $"当前还有以下未完成任务：\n\n{ActivityCenterService.DescribeActiveItems()}\n\n确定退出并终止这些任务吗？",
                showCancelButton: true,
                confirmText: "仍然退出");
            if (result != MessageBoxResult.OK) return;
        }

        IsExiting = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        globalHotkeyService?.Dispose();
        trayIconService?.Dispose();
        singleInstanceService?.Dispose();
        base.OnExit(e);
    }

    private static void App_DispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) =>
        e.SetObserved();
}
