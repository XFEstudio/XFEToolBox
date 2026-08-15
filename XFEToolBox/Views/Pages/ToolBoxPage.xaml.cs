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
using XFEToolBox.Client.Profiles.CrossVersionProfiles;
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
            StatusText.Text = _tools.Count == 0 ? "没有符合条件的工具。" : "点击工具卡片即可获取并缓存。";
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
            await CacheToolAsync(card);
    }

    private async Task CacheToolAsync(ToolCardViewModel card)
    {
        string? temporaryPath = null;
        card.IsEnabled = false;
        card.CacheState = "正在获取…";
        StatusText.Text = $"正在获取 {card.Name}…";

        try
        {
            var detailsResponse = await ClientSession.Requester.Request<ToolPackageDetails>("catalogToolDetails", card.Id);
            var package = detailsResponse.Result?.Versions.FirstOrDefault(item => item.Version == card.LatestVersion);
            if (detailsResponse.StatusCode != HttpStatusCode.OK || package is null)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(detailsResponse.Message) ? "服务器没有返回对应版本。" : detailsResponse.Message);

            var cachePath = GetCachePath(card.Package);
            if (!await IsCachedPackageValidAsync(cachePath, package.Sha256))
            {
                var cacheDirectory = Path.GetDirectoryName(cachePath)!;
                Directory.CreateDirectory(cacheDirectory);
                temporaryPath = Path.Combine(cacheDirectory, $".{Guid.NewGuid():N}.download");

                using var client = new HttpClient
                {
                    BaseAddress = new Uri(SystemProfile.ServerAddress.TrimEnd('/') + "/"),
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

            card.CacheState = "已缓存";
            StatusText.Text = $"{card.Name} {card.LatestVersion} 已缓存。";
        }
        catch (Exception exception)
        {
            card.CacheState = "重试";
            StatusText.Text = $"获取失败：{exception.Message}";
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
        var image = new BitmapImage(new Uri("pack://application:,,,/Resources/Image/wrench_tool.png", UriKind.Absolute));
        image.Freeze();
        return image;
    }
}
