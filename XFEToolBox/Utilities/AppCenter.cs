using System.Windows;
using XFEToolBox.Client.Views.Windows;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;

namespace XFEToolBox.Client.Utilities;

public static class AppCenter
{
    /// <summary>
    /// 关闭窗口退出应用
    /// </summary>
    /// <param name="forceExit">强制退出</param>
    /// <returns>是否成功退出</returns>
    public static bool ExitApp(bool forceExit)
    {
        if (Application.Current is App app)
        {
            if (forceExit) app.RequestExit();
            else MainWindow.Current?.Close();
            return app.IsExiting;
        }

        return false;
    }
}
