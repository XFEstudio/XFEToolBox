using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XFEExtension.NetCore.InputSimulator;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Views.Pages;
using XFEToolBox.Client.Views.Pages.Admin;
using XFEToolBox.Client.Views.Windows;
using XFEToolBox.Client.Utilities.Server;

namespace XFEToolBox.Client.ViewModel.Windows;

public partial class MainWindowViewModel : ObservableObject
{
    private bool isDoubleClickMouseDown = false;
    private int mouseOriginalX = 0;
    private int mouseOriginalY = 0;
    public MainWindow ViewPage { get; set; }

    [ObservableProperty]
    private Page? currentPage = MainPage.Current;

    public string CurrentUserName => ClientSession.CurrentUser?.NickName ?? "尚未登录";

    public string CurrentUserRole => ClientSession.IsAdministrator ? "管理员" : ClientSession.IsLoggedIn ? "普通用户" : "连接工具服务器";

    public bool IsCurrentUserOnline => ClientSession.IsLoggedIn;

    public Visibility AdministratorVisibility => ClientSession.IsAdministrator ? Visibility.Visible : Visibility.Collapsed;

    public MainWindowViewModel(MainWindow viewPage)
    {
        ViewPage = viewPage;
        ViewPage.Closing += ViewPage_Closing;
        ClientSession.SessionChanged += ClientSession_SessionChanged;
        _ = RestoreSessionAsync();
    }

    private static async Task RestoreSessionAsync() => await ClientSession.TryRestoreAsync();

    private void ClientSession_SessionChanged(object? sender, EventArgs e)
    {
        ViewPage.Dispatcher.Invoke(() =>
        {
            OnPropertyChanged(nameof(CurrentUserName));
            OnPropertyChanged(nameof(CurrentUserRole));
            OnPropertyChanged(nameof(IsCurrentUserOnline));
            OnPropertyChanged(nameof(AdministratorVisibility));
        });
    }

    private void ViewPage_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (Application.Current is not App app || app.IsExiting) return;
        e.Cancel = true;
        if (SystemProfile.CloseToTray)
        {
            app.HideMainWindowToTray();
            return;
        }

        app.RequestExit();
    }

    /// <summary>
    /// 最小化窗体
    /// </summary>
    public void Minimize() => ViewPage!.WindowState = WindowState.Minimized;
    /// <summary>
    /// 关闭窗体
    /// </summary>
    public static void CloseWindow() => MainWindow.Current?.Close();
    /// <summary>
    /// 获取窗体DPI缩放
    /// </summary>
    public void GetDPIScale() => SystemProfile.CurrentWindowDPIScale = InputSimulator.GetScalingFactorForWindow(new WindowInteropHelper(ViewPage).Handle);
    public void InitMousePosition()
    {
        var mousePosition = InputSimulator.GetMousePosition();
        mouseOriginalX = mousePosition.X;
        mouseOriginalY = mousePosition.Y;
    }
    /// <summary>
    /// 判断是否双击
    /// </summary>
    /// <param name="delay">间隔</param>
    /// <returns></returns>
    public bool CheckDoubleClick(int delay)
    {
        if (isDoubleClickMouseDown)
        {
            var nowMousePosition = InputSimulator.GetMousePosition();
            if (mouseOriginalX == nowMousePosition.X && mouseOriginalY == nowMousePosition.Y)
                return true;
            else
                return false;
        }
        else
        {
            isDoubleClickMouseDown = true;
            InitMousePosition();
            _ = Task.Run(async () =>
            {
                await Task.Delay(delay);
                isDoubleClickMouseDown = false;
            });
            return false;
        }
    }
    #region Command
    [RelayCommand]
    private void NavigateToPage(string pageTag)
    {
        Page? destination = pageTag switch
        {
            "home" => MainPage.Current,
            "chat" => ChatPage.Current,
            "tool" => ToolBoxPage.Current,
            "workshop" => ToolWorkshopPage.Current,
            "download" => DownloadPage.Current,
            "console" => ConsolePage.Current,
            "setting" => SettingPage.Current,
            "profile" => PersonalCenterPage.Current,
            "serverManagement" when ClientSession.IsAdministrator => ServerManagementPage.Current,
            "serverOverview" when ClientSession.IsAdministrator => ServerOverviewPage.Current,
            "userManagement" when ClientSession.IsAdministrator => UserManagementPage.Current,
            "toolManagement" when ClientSession.IsAdministrator => ToolManagementPage.Current,
            "softwareManagement" when ClientSession.IsAdministrator => SoftwareManagementPage.Current,
            _ => null
        };
        if (destination is null) return;

        CurrentPage = destination;
    }

    [RelayCommand]
    private async Task Logout() => await ClientSession.LogoutAsync();
    #endregion
}
