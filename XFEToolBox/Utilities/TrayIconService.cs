using System.Drawing;
using System.Windows;
using XFEToolBox.Client.Views.Windows;
using Forms = System.Windows.Forms;

namespace XFEToolBox.Client.Utilities;

internal sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly TrayMenuWindow menuWindow;

    public TrayIconService(Action showHome, Action showPalette, Action exit)
    {
        menuWindow = new TrayMenuWindow(showHome, showPalette, exit);

        var icon = Environment.ProcessPath is { } processPath
            ? Icon.ExtractAssociatedIcon(processPath)
            : null;
        notifyIcon = new Forms.NotifyIcon
        {
            Text = "XFEToolBox",
            Icon = icon ?? SystemIcons.Application,
            Visible = true
        };
        notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                Dispatch(showHome);
            else if (e.Button == Forms.MouseButtons.Right)
                Dispatch(menuWindow.ShowAtCursor);
        };
    }

    public void ShowCloseHint() => notifyIcon.ShowBalloonTip(
        3000,
        "XFEToolBox 仍在运行",
        "可使用全局快捷键打开命令面板，或从托盘菜单退出。",
        Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        menuWindow.Close();
    }

    private static void Dispatch(Action action) =>
        Application.Current?.Dispatcher.BeginInvoke(action);
}
