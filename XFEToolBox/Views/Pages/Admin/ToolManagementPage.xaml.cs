using System.Net;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Client.Views.Windows;
using XFEToolBox.Core.Tools;

namespace XFEToolBox.Client.Views.Pages.Admin;

public partial class ToolManagementPage : Page
{
    public static ToolManagementPage Current { get; } = new();

    public ToolManagementPage() => InitializeComponent();

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void OpenEditorButton_Click(object sender, RoutedEventArgs e) => new ToolCodeEditorWindow().Show();

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "XFEToolBox 工具包 (*.xfetool)|*.xfetool", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            StatusText.Text = "正在上传并校验工具包…";
            var bytes = await File.ReadAllBytesAsync(dialog.FileName);
            var response = await ClientSession.Requester.Request<ToolPackageUploadResult>(
                "adminUploadTool", Convert.ToBase64String(bytes), true, true);
            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Created))
            {
                StatusText.Text = response.Message;
                return;
            }
            StatusText.Text = $"已上传 {response.Result.Manifest.Name} {response.Result.Manifest.Version}";
            await RefreshAsync(keepStatus: true);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"上传失败：{exception.Message}";
        }
    }

    private async void TogglePublicationButton_Click(object sender, RoutedEventArgs e)
    {
        if (ToolGrid.SelectedItem is not ToolPackageUploadResult package)
        {
            StatusText.Text = "请先选择一个工具版本。";
            return;
        }
        var response = await ClientSession.Requester.Request<ToolPackageUploadResult>(
            "adminSetPublication", package.Manifest.Id, package.Manifest.Version, !package.Package.Published);
        StatusText.Text = response.StatusCode == HttpStatusCode.OK ? "发布状态已更新。" : response.Message;
        if (response.StatusCode == HttpStatusCode.OK) await RefreshAsync(keepStatus: true);
    }

    private async Task RefreshAsync(bool keepStatus = false)
    {
        if (!keepStatus) StatusText.Text = "正在读取工具包…";
        try
        {
            var response = await ClientSession.Requester.Request<ToolPackageUploadResult[]>("adminTools");
            if (response.StatusCode != HttpStatusCode.OK)
            {
                StatusText.Text = response.Message;
                return;
            }
            ToolGrid.ItemsSource = response.Result ?? [];
            if (!keepStatus) StatusText.Text = $"共 {response.Result?.Length ?? 0} 个工具版本。";
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
    }
}
