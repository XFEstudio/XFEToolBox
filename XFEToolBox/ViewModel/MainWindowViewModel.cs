using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using System.Windows.Controls;
using XFEToolBox.Profiles.CrossVersionProfiles;
using XFEToolBox.Views.Pages;
using XFEToolBox.Views.Windows;

namespace XFEToolBox.ViewModel;

public partial class MainWindowViewModel : ObservableObject
{
    public MainWindow? ViewPage { get; set; }

    [ObservableProperty]
    private Page? currentPage = MainPage.Current;

    public MainWindowViewModel(MainWindow viewPage)
    {
        ViewPage = viewPage;
    }
    /// <summary>
    /// 最小化窗体
    /// </summary>
    public void Minimize() => ViewPage!.WindowState = WindowState.Minimized;
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
    /// <summary>
    /// 关闭窗体
    /// </summary>
    public void CloseWindow()
    {
        ExitApp(true);
        //TODO:1 待完善的退出应用逻辑，强制退出等
    }

    [RelayCommand]
    private void NavigateToPage(string pageTag)
    {
        switch (pageTag)
        {
            case "home":
                CurrentPage = MainPage.Current;
                break;
            case "tool":
                CurrentPage = MainPage.Current;
                break;
            case "download":
                CurrentPage = MainPage.Current;
                break;
            case "setting":
                CurrentPage = SettingPage.Current;
                break;
            default:
                break;
        }
    }
}
