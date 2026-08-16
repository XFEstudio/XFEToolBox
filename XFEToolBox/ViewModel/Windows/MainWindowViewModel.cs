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
            OnPropertyChanged(nameof(AdministratorVisibility));
        });
    }

    private void ViewPage_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (TaskManager.TaskDictionary.Count > 0)
        {
            e.Cancel = true;
            if (PopupHelper.ShowConfirmDialog($"当前还有以下未完成的任务：\n\n{string.Join(",\n", TaskManager.TaskDictionary.Select(d => $"ID：{d.Value.Task.Id}\t 名称：{d.Value.Name}\t 状态：{d.Value.Task.Status}"))}\n\n是否仍要关闭？", true) == MessageBoxResult.OK)
                AppCenter.ExitApp(true);
        }
    }

    /// <summary>
    /// 最小化窗体
    /// </summary>
    public void Minimize() => ViewPage!.WindowState = WindowState.Minimized;
    /// <summary>
    /// 关闭窗体
    /// </summary>
    public static void CloseWindow()
    {
        AppCenter.ExitApp(false);
    }
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
        switch (pageTag)
        {
            case "home":
                CurrentPage = MainPage.Current;
                break;
            case "tool":
                CurrentPage = ToolBoxPage.Current;
                break;
            case "download":
                CurrentPage = DownloadPage.Current;
                break;
            case "console":
                CurrentPage = ConsolePage.Current;
                break;
            case "setting":
                CurrentPage = SettingPage.Current;
                break;
            case "profile":
                CurrentPage = PersonalCenterPage.Current;
                break;
            case "serverManagement":
                if (ClientSession.IsAdministrator) CurrentPage = ServerManagementPage.Current;
                break;
            case "serverOverview":
                if (ClientSession.IsAdministrator) CurrentPage = ServerOverviewPage.Current;
                break;
            case "userManagement":
                if (ClientSession.IsAdministrator) CurrentPage = UserManagementPage.Current;
                break;
            case "toolManagement":
                if (ClientSession.IsAdministrator) CurrentPage = ToolManagementPage.Current;
                break;
            case "softwareManagement":
                if (ClientSession.IsAdministrator) CurrentPage = SoftwareManagementPage.Current;
                break;
            default:
                break;
        }
    }

    [RelayCommand]
    private void Logout() => ClientSession.Logout();
    #endregion
}
