using System.Net;
using System.Windows;
using System.Windows.Controls;
using XFEToolBox.Client.Models.Server;
using XFEToolBox.Client.Utilities.Server;

namespace XFEToolBox.Client.Views.Pages.Admin;

public partial class ServerOverviewPage : Page
{
    public static ServerOverviewPage Current { get; } = new();

    public ServerOverviewPage() => InitializeComponent();

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        ErrorText.Text = string.Empty;
        StatusValue.Text = "正在读取…";
        try
        {
            var response = await ClientSession.Requester.Request<AdminOverview>("adminOverview");
            if (response.StatusCode != HttpStatusCode.OK || response.Result is null)
            {
                ErrorText.Text = response.Message;
                StatusValue.Text = "不可用";
                return;
            }

            var overview = response.Result;
            StatusValue.Text = overview.Status == "running" ? "运行中" : overview.Status;
            UsersValue.Text = $"{overview.UserCount} / {overview.ActiveSessionCount}";
            PackagesValue.Text = $"{overview.PackageCount} / {overview.PublishedPackageCount}";
            StorageValue.Text = FormatBytes(overview.StorageBytes);
            ServerNameValue.Text = overview.ServerName;
            UpdatedValue.Text = $"最后更新：{overview.Utc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        }
        catch (Exception exception)
        {
            ErrorText.Text = $"读取服务器状态失败：{exception.Message}";
            StatusValue.Text = "不可用";
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):F2} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):F2} MB",
        >= 1024L => $"{bytes / 1024d:F2} KB",
        _ => $"{bytes} B"
    };
}
