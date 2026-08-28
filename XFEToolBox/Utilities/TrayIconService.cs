using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace XFEToolBox.Client.Utilities;

internal sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon notifyIcon;

    public TrayIconService(Action showHome, Action showPalette, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开主页", null, (_, _) => Dispatch(showHome));
        menu.Items.Add("打开命令面板", null, (_, _) => Dispatch(showPalette));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatch(exit));

        var icon = Environment.ProcessPath is { } processPath
            ? Icon.ExtractAssociatedIcon(processPath)
            : null;
        notifyIcon = new Forms.NotifyIcon
        {
            Text = "XFEToolBox",
            Icon = icon ?? SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        notifyIcon.DoubleClick += (_, _) => Dispatch(showHome);
    }

    public void ShowCloseHint() => notifyIcon.ShowBalloonTip(
        3000,
        "XFEToolBox 仍在运行",
        "可使用全局快捷键打开命令面板，或从托盘菜单退出。",
        Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        notifyIcon.Visible = false;
        notifyIcon.ContextMenuStrip?.Dispose();
        notifyIcon.Dispose();
    }

    private static void Dispatch(Action action) =>
        Application.Current?.Dispatcher.BeginInvoke(action);
}
