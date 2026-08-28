using System.Net;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Client.Views.Pages.Popups;
using XFEToolBox.Client.Views.Windows;
using XFEToolBox.Core.Tools;

namespace XFEToolBox.Client.Views.Pages.Admin;

public partial class ToolManagementPage : Page
{
    public static ToolManagementPage Current { get; } = new();

    public ToolManagementPage() => InitializeComponent();

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void OpenControlGalleryButton_Click(object sender, RoutedEventArgs e) =>
        ControlGalleryWindow.ShowGallery(Window.GetWindow(this));

    private void OpenProjectsButton_Click(object sender, RoutedEventArgs e)
    {
        var launcher = new ToolProjectLauncherPopupPage();
        var result = PopupHelper.ShowDialog(launcher, new PopupWindowOptions
        {
            Title = "工具项目",
            Subtitle = "选择历史项目或浏览本地项目",
            Width = 700,
            Height = 520,
            ContentMargin = new Thickness(0)
        });
        if (launcher.CreateProjectRequested)
        {
            ShowNewToolDialog();
            return;
        }
        if (result == MessageBoxResult.OK && launcher.SelectedProjectPath is not null)
            OpenEditor(launcher.SelectedProjectPath);
    }

    private static void ShowNewToolDialog()
    {
        var creator = new NewToolProjectPopupPage();
        var result = PopupHelper.ShowDialog(creator, new PopupWindowOptions
        {
            Title = "新建工具",
            Subtitle = "创建标准 XFEToolBox 工具工程",
            Width = 620,
            Height = 430,
            ContentMargin = new Thickness(0)
        });
        if (result == MessageBoxResult.OK && creator.CreatedProjectPath is not null)
            OpenEditor(creator.CreatedProjectPath);
    }

    private static void OpenEditor(string projectPath)
    {
        var editor = new ToolCodeEditorWindow(projectPath);
        editor.Show();
        editor.Activate();
    }

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "XFEToolBox 工具包 (*.xfetool)|*.xfetool", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            StatusText.Text = "正在上传并校验工具包…";
            var bytes = await File.ReadAllBytesAsync(dialog.FileName);
            var response = await ClientSession.Requester.Request<ToolPackageUploadResult>(
                "adminUploadTool", Convert.ToBase64String(bytes), true, false);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var message = string.IsNullOrWhiteSpace(response.Message)
                    ? "服务器中已存在相同的工具版本，请修改工具包 manifest.json 中的 version 后重新上传。"
                    : response.Message;
                StatusText.Text = message;
                PopupHelper.ShowConfirmDialog(message, confirmText: "知道了");
                return;
            }
            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Created) || response.Result is null)
            {
                StatusText.Text = string.IsNullOrWhiteSpace(response.Message) ? "服务器没有返回工具包信息。" : response.Message;
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

    private async void ApproveReviewButton_Click(object sender, RoutedEventArgs e) => await ReviewSelectedAsync(true);

    private async void RejectReviewButton_Click(object sender, RoutedEventArgs e) => await ReviewSelectedAsync(false);

    private async Task ReviewSelectedAsync(bool approved)
    {
        if (ToolGrid.SelectedItem is not ToolPackageUploadResult package)
        {
            StatusText.Text = "请先选择一个工具版本。";
            return;
        }
        var response = await ClientSession.Requester.Request<ToolPackageUploadResult>(
            "adminReviewTool", package.Manifest.Id, package.Manifest.Version, approved, null!);
        StatusText.Text = response.StatusCode == HttpStatusCode.OK
            ? approved ? "审核已通过，版本已公开。" : "审核已拒绝，版本不会公开。"
            : response.Message;
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
