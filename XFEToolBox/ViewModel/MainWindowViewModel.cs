using CommunityToolkit.Mvvm.ComponentModel;
using XFEToolBox.Views.Pages;
using XFEToolBox.Views.Windows;
using System.Windows.Controls;
using System.Windows;
using XFEToolBox.Profiles;

namespace XFEToolBox.ViewModel;

public partial class MainWindowViewModel : ObservableObject
{
    public MainWindow? ViewPage { get; set; }

    [ObservableProperty]
    private Page? currentPage = new MainPage();

    public MainWindowViewModel(MainWindow viewPage)
    {
        ViewPage = viewPage;
    }
    /// <summary>
    /// 最小化窗体
    /// </summary>
    public void Minimize() => ViewPage!.WindowState = System.Windows.WindowState.Minimized;
    /// <summary>
    /// 关闭窗口退出应用
    /// </summary>
    /// <param name="forceExit">强制退出</param>
    /// <returns>是否成功退出</returns>
    public static bool ExitApp(bool forceExit)
    {
        if (forceExit || SystemProfile.CanClosed)
        {
            Application.Current.Shutdown();
            return true;
        }
        else
        {
            return false;
        }
    }
    public void CloseWindow()
    {
        ExitApp(true);
        //TODO:1 待完善的退出应用逻辑，强制退出等
    }

    public void MoveWindow(int x, int y)
    {
        
    }
}
