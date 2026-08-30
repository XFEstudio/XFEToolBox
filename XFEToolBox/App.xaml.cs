using System.Windows;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Utilities.Chat;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Client.Views.Windows;

namespace XFEToolBox.Client;

public partial class App : Application
{
    private SingleInstanceService? singleInstanceService;
    private TrayIconService? trayIconService;
    private GlobalHotkeyService? globalHotkeyService;
    private CommandPaletteWindow? commandPaletteWindow;
    private MainWindow? mainWindow;
    private bool chatRuntimeInitialized;
    private bool chatRuntimeEventsDetached;
    private Task? chatRuntimeShutdownTask;
    private ChatDesktopNotificationCoordinator? chatNotificationCoordinator;
    private readonly SemaphoreSlim chatSessionGate = new(1, 1);

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
        InitializeChatRuntime();
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
        chatNotificationCoordinator = new ChatDesktopNotificationCoordinator();
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

    public void ShowDesktopNotification(string title, string message, DesktopNotificationLevel level = DesktopNotificationLevel.Information) =>
        trayIconService?.ShowNotification(title, message, level);

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

    public async void RequestExit()
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
        try
        {
            await ShutdownChatRuntimeAsync();
        }
        finally
        {
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (chatRuntimeInitialized)
        {
            DetachChatRuntimeEvents();
            ChatRealtimeClient.Shared.RequestStop();
        }
        globalHotkeyService?.Dispose();
        chatNotificationCoordinator?.Dispose();
        trayIconService?.Dispose();
        singleInstanceService?.Dispose();
        base.OnExit(e);
    }

    private void InitializeChatRuntime()
    {
        if (chatRuntimeInitialized) return;
        chatRuntimeInitialized = true;
        ClientSession.SessionChanged += ClientSession_SessionChanged;
        VoiceCallService.Current.IncomingCallReceived += VoiceCallService_IncomingCallReceived;
        VoiceCallService.Current.Error += VoiceCallService_Error;
    }

    private Task ShutdownChatRuntimeAsync()
    {
        if (!chatRuntimeInitialized) return Task.CompletedTask;
        return chatRuntimeShutdownTask ??= ShutdownChatRuntimeCoreAsync();
    }

    private async Task ShutdownChatRuntimeCoreAsync()
    {
        DetachChatRuntimeEvents();
        await chatSessionGate.WaitAsync();
        try
        {
            try
            {
                await VoiceCallService.Current.DisposeAsync();
            }
            catch
            {
            }

            try
            {
                await ChatRealtimeClient.Shared.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch
            {
                ChatRealtimeClient.Shared.RequestStop();
            }
        }
        finally
        {
            chatSessionGate.Release();
        }
    }

    private void DetachChatRuntimeEvents()
    {
        if (chatRuntimeEventsDetached) return;
        chatRuntimeEventsDetached = true;
        ClientSession.SessionChanged -= ClientSession_SessionChanged;
        VoiceCallService.Current.IncomingCallReceived -= VoiceCallService_IncomingCallReceived;
        VoiceCallService.Current.Error -= VoiceCallService_Error;
    }

    private async void ClientSession_SessionChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            var operation = Dispatcher.InvokeAsync(UpdateChatRealtimeSessionAsync);
            await await operation;
            return;
        }
        await UpdateChatRealtimeSessionAsync();
    }

    private async Task UpdateChatRealtimeSessionAsync()
    {
        await chatSessionGate.WaitAsync();
        try
        {
            if (IsExiting) return;
            if (ClientSession.IsLoggedIn)
            {
                await VoiceCallService.Current.StartAsync();
                return;
            }

            try { await VoiceCallService.Current.LeaveAsync(); }
            catch { }
            await ChatRealtimeClient.Shared.StopAsync();
        }
        finally
        {
            chatSessionGate.Release();
        }
    }

    private async void VoiceCallService_IncomingCallReceived(object? sender, IncomingVoiceCallEventArgs e)
    {
        if (!ClientSession.IsLoggedIn) return;
        var invitation = e.Invitation;
        var result = PopupHelper.ShowYesOrNoDialog(
            $"{invitation.FromDisplayName} 邀请你加入语音通话。",
            showCancelButton: false,
            yesText: "接听",
            noText: "拒绝");
        try
        {
            if (result == MessageBoxResult.Yes)
                await VoiceCallService.Current.AcceptAsync(invitation.CallId);
            else
                await VoiceCallService.Current.RejectAsync(invitation.CallId);
        }
        catch (Exception exception)
        {
            PopupHelper.ShowConfirmDialog($"无法处理通话邀请：{exception.Message}", confirmText: "知道了");
        }
    }

    private void VoiceCallService_Error(object? sender, VoiceCallErrorEventArgs e)
    {
        if (!IsExiting)
            PopupHelper.ShowConfirmDialog(e.Message, confirmText: "知道了");
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
