using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using XFEToolBox.Client.Models.Server;
using XFEToolBox.Client.Utilities.Server;

namespace XFEToolBox.Client.Views.Pages.Admin;

public partial class ServerOverviewPage : Page
{
    public static ServerOverviewPage Current { get; } = new();
    private readonly DispatcherTimer refreshTimer;
    private bool isRefreshing;
    private bool hasSnapshot;

    public ServerOverviewPage()
    {
        InitializeComponent();
        refreshTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        refreshTimer.Tick += RefreshTimer_Tick;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        refreshTimer.Start();
        await RefreshAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e) => refreshTimer.Stop();

    private async void RefreshTimer_Tick(object? sender, EventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (isRefreshing || !ClientSession.IsAdministrator) return;
        isRefreshing = true;
        ErrorText.Text = string.Empty;
        try
        {
            var response = await ClientSession.Requester.Request<AdminOverview>("adminOverview");
            if (response.StatusCode != HttpStatusCode.OK || response.Result is null)
            {
                ErrorText.Text = response.Message;
                if (!hasSnapshot) StatusValue.Text = "不可用";
                LiveIndicator.Fill = new SolidColorBrush(Color.FromRgb(239, 112, 112));
                return;
            }

            var overview = response.Result;
            StatusValue.Text = overview.Status == "running" ? "运行中" : overview.Status;
            UptimeValue.Text = $"已持续运行 {FormatDuration(overview.UptimeSeconds)}";
            UsersValue.Text = $"{overview.UserCount} / {overview.ActiveSessionCount}";
            UsersDetailValue.Text = $"{overview.UserCount} 个注册用户 · {overview.ActiveSessionCount} 个有效会话";
            PackagesValue.Text = $"{overview.PublishedPackageCount} 工具 · {overview.PublishedSoftwareCount} 软件";
            PackagesDetailValue.Text = $"共 {overview.PackageCount} 个工具版本 · {overview.SoftwareCount} 个软件";
            StorageValue.Text = FormatBytes(overview.StorageBytes);
            StorageDetailValue.Text = $"软件文件 {FormatBytes(overview.SoftwareStorageBytes)} · 其余为工具包";
            CpuValue.Text = $"{overview.CpuUsagePercent:F1}%";
            CpuUsageBar.Value = overview.CpuUsagePercent;
            CpuDetailValue.Text = $"{overview.ProcessorCount} 个逻辑核心 · 操作系统整体实时采样";
            MemoryValue.Text = $"{overview.MemoryUsagePercent:F1}%";
            MemoryUsageBar.Value = overview.MemoryUsagePercent;
            MemoryDetailValue.Text = $"已用 {FormatBytes(overview.UsedMemoryBytes)} / {FormatBytes(overview.TotalMemoryBytes)}\n可用 {FormatBytes(overview.AvailableMemoryBytes)}";
            ProcessMemoryValue.Text = $"服务器进程：{FormatBytes(overview.WorkingSetBytes)}";
            ServerNameValue.Text = overview.ServerName;
            UpdatedValue.Text = $"最后更新：{overview.Utc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
            LiveIndicator.Fill = new SolidColorBrush(Color.FromRgb(113, 238, 162));
            hasSnapshot = true;
        }
        catch (Exception exception)
        {
            ErrorText.Text = $"读取服务器状态失败：{exception.Message}";
            if (!hasSnapshot) StatusValue.Text = "不可用";
            LiveIndicator.Fill = new SolidColorBrush(Color.FromRgb(239, 112, 112));
        }
        finally { isRefreshing = false; }
    }

    private static string FormatDuration(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (value.TotalDays >= 1) return $"{(int)value.TotalDays} 天 {value.Hours} 小时";
        if (value.TotalHours >= 1) return $"{value.Hours} 小时 {value.Minutes} 分";
        return $"{value.Minutes} 分 {value.Seconds} 秒";
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):F2} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):F2} MB",
        >= 1024L => $"{bytes / 1024d:F2} KB",
        _ => $"{bytes} B"
    };
}
