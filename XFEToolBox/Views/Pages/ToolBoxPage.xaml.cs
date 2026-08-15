using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Core.Tools;

namespace XFEToolBox.Client.Views.Pages;

public partial class ToolBoxPage : Page
{
    public static ToolBoxPage Current { get; private set; } = new();

    public ToolBoxPage()
    {
        Current = this;
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) await RefreshAsync(); }

    private async Task RefreshAsync()
    {
        try
        {
            StatusText.Text = "正在连接工具服务器…";
            var response = await ClientSession.Requester.Request<ToolPackageSummary[]>(
                "catalogTools", SearchTextBox.Text.Trim(), string.Empty);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                StatusText.Text = response.Message;
                return;
            }
            ToolGrid.ItemsSource = response.Result ?? [];
            StatusText.Text = $"共找到 {response.Result?.Length ?? 0} 个工具。";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"读取失败：{exception.Message}";
        }
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (ToolGrid.SelectedItem is not ToolPackageSummary tool)
        {
            StatusText.Text = "请先选择一个工具。";
            return;
        }
        try
        {
            var detailsResponse = await ClientSession.Requester.Request<ToolPackageDetails>("catalogToolDetails", tool.Id);
            var version = detailsResponse.Result?.Versions.FirstOrDefault(item => item.Version == tool.LatestVersion);
            if (detailsResponse.StatusCode != HttpStatusCode.OK || version is null)
            {
                StatusText.Text = detailsResponse.Message;
                return;
            }
            var dialog = new SaveFileDialog
            {
                Filter = "XFEToolBox 工具包 (*.xfetool)|*.xfetool",
                FileName = $"{tool.Id}-{tool.LatestVersion}.xfetool",
                AddExtension = true
            };
            if (dialog.ShowDialog() != true) return;
            using var client = new HttpClient { BaseAddress = new Uri(SystemProfile.ServerAddress.TrimEnd('/') + "/") };
            using var response = await client.PostAsJsonAsync("v1/tools/download", new { toolId = tool.Id, version = tool.LatestVersion });
            response.EnsureSuccessStatusCode();
            await using var output = File.Create(dialog.FileName);
            await response.Content.CopyToAsync(output);
            StatusText.Text = $"已下载 {tool.Name} {tool.LatestVersion}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"下载失败：{exception.Message}";
        }
    }
}
