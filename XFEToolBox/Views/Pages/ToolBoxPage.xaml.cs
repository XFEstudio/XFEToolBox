using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Client.ViewModel.Pages;
using XFEToolBox.Core.Model;
using XFEToolBox.Core.Tools;

namespace XFEToolBox.Client.Views.Pages;

public partial class ToolBoxPage : Page
{
    private static readonly ImageSource DefaultToolIcon = CreateDefaultIcon();
    private readonly ObservableCollection<ToolCardViewModel> _tools = [];

    public static ToolBoxPage Current { get; private set; } = new();

    public ToolBoxPage()
    {
        Current = this;
        InitializeComponent();
        ToolCards.ItemsSource = _tools;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            StatusText.Text = "正在连接工具服务器…";
            var response = await ClientSession.Requester.Request<ToolPackageSummary[]>(
                "catalogTools", SearchTextBox.Text.Trim(), string.Empty);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                ShowEmptyResult(response.Message);
                return;
            }

            _tools.Clear();
            foreach (var tool in response.Result ?? [])
            {
                var cached = File.Exists(GetCachePath(tool));
                _tools.Add(new ToolCardViewModel(tool, CreateIconSource(tool.IconDataUrl), cached));
            }

            EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ToolCountText.Text = $"{_tools.Count} 个工具";
            StatusText.Text = _tools.Count == 0 ? "没有符合条件的工具。" : "点击工具卡片即可打开；未缓存的工具会先自动获取。";
        }
        catch (Exception exception)
        {
            ShowEmptyResult($"读取失败：{exception.Message}");
        }
    }

    private void ShowEmptyResult(string message)
    {
        _tools.Clear();
        EmptyState.Visibility = Visibility.Visible;
        ToolCountText.Text = string.Empty;
        StatusText.Text = message;
    }

    private async void ToolCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: ToolCardViewModel card })
            await OpenToolAsync(card);
    }

    private async Task OpenToolAsync(ToolCardViewModel card)
    {
        string? temporaryPath = null;
        string? cachePath = null;
        card.IsEnabled = false;
        card.CacheState = "正在校验…";
        StatusText.Text = $"正在准备 {card.Name}…";

        try
        {
            var detailsResponse = await ClientSession.Requester.Request<ToolPackageDetails>("catalogToolDetails", card.Id);
            var package = detailsResponse.Result?.Versions.FirstOrDefault(item => item.Version == card.LatestVersion);
            if (detailsResponse.StatusCode != HttpStatusCode.OK || package is null)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(detailsResponse.Message) ? "服务器没有返回对应版本。" : detailsResponse.Message);

            cachePath = GetCachePath(card.Package);
            if (!await IsCachedPackageValidAsync(cachePath, package.Sha256))
            {
                card.CacheState = "正在获取…";
                StatusText.Text = $"正在获取 {card.Name} {card.LatestVersion}…";
                var cacheDirectory = Path.GetDirectoryName(cachePath)!;
                Directory.CreateDirectory(cacheDirectory);
                temporaryPath = Path.Combine(cacheDirectory, $".{Guid.NewGuid():N}.download");

                using var client = new HttpClient
                {
                    BaseAddress = new Uri(ClientSession.ApiAddress + "/"),
                    Timeout = TimeSpan.FromMinutes(2)
                };
                var catalogClient = new ToolCatalogClient(client);
                await using (var output = new FileStream(
                                 temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await catalogClient.DownloadPackageAsync(package, output);
                    await output.FlushAsync();
                }
                File.Move(temporaryPath, cachePath, overwrite: true);
                temporaryPath = null;
            }

            card.CacheState = "正在打开…";
            StatusText.Text = $"正在编译并打开 {card.Name}…";
            var runResult = await ToolProjectRunService.BuildPackageAndRunAsync(
                cachePath,
                card.Id,
                package.Version,
                package.Sha256);
            if (!runResult.Success)
                throw new InvalidOperationException(runResult.Message);

            card.CacheState = "已打开";
            StatusText.Text = $"{card.Name} {card.LatestVersion} 已在独立窗口中打开。";
        }
        catch (Exception exception)
        {
            card.CacheState = cachePath is not null && File.Exists(cachePath) ? "重试打开" : "重试获取";
            StatusText.Text = $"打开失败：{exception.Message}";
        }
        finally
        {
            card.IsEnabled = true;
            if (temporaryPath is not null && File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string GetCachePath(ToolPackageSummary tool) => Path.Combine(
        AppPath.CacheProfile,
        "ToolPackages",
        tool.Id,
        $"{tool.LatestVersion}.xfetool");

    private static async Task<bool> IsCachedPackageValidAsync(string path, string expectedSha256)
    {
        if (!File.Exists(path)) return false;
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha256 = SHA256.Create();
        var actual = Convert.ToHexString(await sha256.ComputeHashAsync(stream)).ToLowerInvariant();
        return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static ImageSource CreateIconSource(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl) || !dataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            return DefaultToolIcon;

        try
        {
            var separator = dataUrl.IndexOf(',');
            if (separator < 0) return DefaultToolIcon;
            using var stream = new MemoryStream(Convert.FromBase64String(dataUrl[(separator + 1)..]));
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return DefaultToolIcon;
        }
    }

    private static ImageSource CreateDefaultIcon()
    {
        var assemblyName = typeof(ToolBoxPage).Assembly.GetName().Name;
        var image = new BitmapImage(new Uri(
            $"pack://application:,,,/{assemblyName};component/Resources/Image/wrench_tool.png",
            UriKind.Absolute));
        image.Freeze();
        return image;
    }
}
