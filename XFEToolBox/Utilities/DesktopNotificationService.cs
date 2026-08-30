using System.Windows;

namespace XFEToolBox.Client.Utilities;

public enum DesktopNotificationLevel
{
    Information,
    Warning,
    Error
}

public static class DesktopNotificationService
{
    public static void Show(
        string title,
        string message,
        DesktopNotificationLevel level = DesktopNotificationLevel.Information)
    {
        if (string.IsNullOrWhiteSpace(title) || Application.Current is not App app) return;
        if (app.Dispatcher.CheckAccess())
            app.ShowDesktopNotification(title, message, level);
        else
            app.Dispatcher.BeginInvoke(() => app.ShowDesktopNotification(title, message, level));
    }
}
