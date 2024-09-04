using System.Windows;
using XFEToolBox.Profiles.CrossVersionProfiles;
using XFEToolBox.Views.Windows;

namespace XFEToolBox.Utilities;

public static class AppCenter
{
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
            MainWindow.Current?.Close();
            return false;
        }
    }
}
