using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XFEExtension.NetCore.InputSimulator;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Views.Pages;
using XFEToolBox.Client.Views.Windows;

namespace XFEToolBox.Client.ViewModel.Windows;

public partial class MainWindowViewModel : ObservableObject
{
    private bool isDragMouseDown = false;
    private bool isDoubleClickMouseDown = false;
    private int mouseOriginalX = 0;
    private int mouseOriginalY = 0;
    public MainWindow ViewPage { get; set; }

    [ObservableProperty]
    private Page? currentPage = MainPage.Current;

    public MainWindowViewModel(MainWindow viewPage)
    {
        ViewPage = viewPage;
        ViewPage.Closing += ViewPage_Closing;
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
            default:
                break;
        }
    }
    #endregion
}
