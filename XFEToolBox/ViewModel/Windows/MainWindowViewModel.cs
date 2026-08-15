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
    private bool isDragMouseDown = false;
    private bool isDoubleClickMouseDown = false;
    private int mouseOriginalX = 0;
    private int mouseOriginalY = 0;
    public MainWindow ViewPage { get; set; }

    [ObservableProperty]
    private Page? currentPage = LoginPage.Current;

    public string CurrentUserName => ClientSession.CurrentUser?.NickName ?? "尚未登录";

    public string CurrentUserRole => ClientSession.IsAdministrator ? "管理员" : ClientSession.IsLoggedIn ? "普通用户" : "连接工具服务器";

    public Visibility AuthenticatedVisibility => ClientSession.IsLoggedIn ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AdministratorVisibility => ClientSession.IsAdministrator ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LoginVisibility => ClientSession.IsLoggedIn ? Visibility.Collapsed : Visibility.Visible;

    public Visibility LogoutVisibility => ClientSession.IsLoggedIn ? Visibility.Visible : Visibility.Collapsed;

    public MainWindowViewModel(MainWindow viewPage)
    {
        ViewPage = viewPage;
        ViewPage.Closing += ViewPage_Closing;
        ClientSession.SessionChanged += ClientSession_SessionChanged;
        _ = RestoreSessionAsync();
    }

    private async Task RestoreSessionAsync()
    {
        if (await ClientSession.TryRestoreAsync())
            CurrentPage = MainPage.Current;
    }

    private void ClientSession_SessionChanged(object? sender, EventArgs e)
    {
        ViewPage.Dispatcher.Invoke(() =>
        {
            OnPropertyChanged(nameof(CurrentUserName));
            OnPropertyChanged(nameof(CurrentUserRole));
            OnPropertyChanged(nameof(AuthenticatedVisibility));
            OnPropertyChanged(nameof(AdministratorVisibility));
            OnPropertyChanged(nameof(LoginVisibility));
            OnPropertyChanged(nameof(LogoutVisibility));
            CurrentPage = ClientSession.IsLoggedIn ? MainPage.Current : LoginPage.Current;
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
    /// 初始化调整窗体参数
    /// </summary>
    public void InitializeToResize()
    {
        isDragMouseDown = true;
        InitMousePosition();
        _ = Task.Run(Resize);
    }
    /// <summary>
    /// 调整窗体大小
    /// </summary>
    public void Resize()
    {
        while (isDragMouseDown && InputSimulator.GetMouseDown(MouseButton.Left))
        {
            var nowMousePoint = InputSimulator.GetMousePosition();
            ViewPage.Dispatcher.Invoke(() =>
            {
                double newWidth = ViewPage.Width + (nowMousePoint.X - mouseOriginalX) / SystemProfile.CurrentWindowDPIScale;
                double newHeight = ViewPage.Height + (nowMousePoint.Y - mouseOriginalY) / SystemProfile.CurrentWindowDPIScale;
                if (newWidth > ViewPage.MinWidth)
                    ViewPage.Width = newWidth;
                else
                    ViewPage.Width = ViewPage.MinWidth;
                if (newHeight > ViewPage.MinHeight)
                    ViewPage.Height = newHeight;
                else
                    ViewPage.Height = ViewPage.MinHeight;
            });
            mouseOriginalX = nowMousePoint.X;
            mouseOriginalY = nowMousePoint.Y;
        }
        ViewPage.Dispatcher.Invoke(() =>
        {
            SystemProfile.MainWindowWidth = ViewPage.Width;
            SystemProfile.MainWindowHeight = ViewPage.Height;
        });
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
            case "login":
                CurrentPage = LoginPage.Current;
                break;
            case "editor":
                if (ClientSession.IsAdministrator) new ToolCodeEditorWindow().Show();
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
            default:
                break;
        }
    }

    [RelayCommand]
    private void Logout() => ClientSession.Logout();
    #endregion
}
