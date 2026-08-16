using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Client.ViewModel.Pages;
using XFEToolBox.Client.Profiles.CacheProfiles;
using XFEToolBox.Core.Model;
using XFEToolBox.Core.Tools;

namespace XFEToolBox.Client.Views.Pages;

public partial class ToolBoxPage : Page
{
    private static readonly ImageSource DefaultToolIcon = CreateDefaultIcon();
    private static readonly JsonSerializerOptions CacheJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ObservableCollection<ToolCardViewModel> _tools = [];
    private ToolPackageSummary[] _cachedCatalog = [];
    private bool _cacheLoaded;
    private bool _hasCachedCatalog;
    private int _refreshGeneration;

    public static ToolBoxPage Current { get; private set; } = new();

    public ToolBoxPage()
    {
        Current = this;
        InitializeComponent();
        ToolCards.ItemsSource = _tools;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync(refreshFullCatalog: true);
    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync(refreshFullCatalog: true);
    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await RefreshAsync();
    }

    private async Task RefreshAsync(bool refreshFullCatalog = false)
    {
        EnsureCacheLoaded();
        var query = SearchTextBox.Text.Trim();
        if (_hasCachedCatalog)
        {
            ApplyTools(FilterCachedTools(query));
            StatusText.Text = _tools.Count == 0
                ? "缓存中没有符合条件的工具，正在后台刷新…"
                : "已显示本地缓存，正在后台刷新…";
        }
        else
        {
            StatusText.Text = "正在连接工具服务器…";
        }

        var refreshGeneration = ++_refreshGeneration;
        try
        {
            var requestQuery = refreshFullCatalog ? string.Empty : query;
            var response = await ClientSession.Requester.Request<ToolPackageSummary[]>(
                "catalogTools", requestQuery, string.Empty);
            if (refreshGeneration != _refreshGeneration) return;
            if (response.StatusCode != HttpStatusCode.OK)
            {
                ShowRefreshFailure(response.Message);
                return;
            }

            var tools = response.Result ?? [];
            if (refreshFullCatalog || string.IsNullOrWhiteSpace(query))
            {
                _cachedCatalog = tools;
                _hasCachedCatalog = true;
                TrySaveCatalogCache(tools);
                tools = FilterCachedTools(query);
            }

            ApplyTools(tools);
            StatusText.Text = _tools.Count == 0
                ? "没有符合条件的工具。"
                : "点击工具卡片即可打开；未缓存的工具会先自动获取。右键卡片可清除该工具的数据。";
        }
        catch (Exception exception)
        {
            if (refreshGeneration == _refreshGeneration)
                ShowRefreshFailure($"读取失败：{exception.Message}");
        }
    }

    private void EnsureCacheLoaded()
    {
        if (_cacheLoaded) return;
        _cacheLoaded = true;
        try
        {
            var json = AppCacheProfile.ToolCatalogJson;
            if (string.IsNullOrWhiteSpace(json)) return;
            _cachedCatalog = JsonSerializer.Deserialize<ToolPackageSummary[]>(json, CacheJsonOptions) ?? [];
            _hasCachedCatalog = true;
        }
        catch
        {
            _cachedCatalog = [];
            _hasCachedCatalog = false;
        }
    }

    private ToolPackageSummary[] FilterCachedTools(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return _cachedCatalog;
        return _cachedCatalog.Where(tool =>
                Contains(tool.Name, query)
                || Contains(tool.Description, query)
                || Contains(tool.Author, query)
                || Contains(tool.Category, query)
                || tool.Tags?.Any(tag => Contains(tag, query)) == true)
            .ToArray();
    }

    private void ApplyTools(IReadOnlyList<ToolPackageSummary> tools)
    {
        var desiredCards = tools.Select(tool =>
        {
            var existing = _tools.FirstOrDefault(card =>
                string.Equals(card.Id, tool.Id, StringComparison.OrdinalIgnoreCase)
                && ToolSummariesEquivalent(card.Package, tool));
            if (existing is not null) return existing;
            return new ToolCardViewModel(tool, CreateIconSource(tool.IconDataUrl), File.Exists(GetCachePath(tool)));
        }).ToArray();

        ReconcileCollection(_tools, desiredCards);
        EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ToolCountText.Text = $"{_tools.Count} 个工具";
    }

    private void ShowRefreshFailure(string message)
    {
        if (_hasCachedCatalog)
        {
            EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ToolCountText.Text = $"{_tools.Count} 个工具";
            StatusText.Text = $"后台刷新失败，当前显示本地缓存：{message}";
            return;
        }

        ShowEmptyResult(message);
    }

    private static void TrySaveCatalogCache(ToolPackageSummary[] tools)
    {
        try
        {
            AppCacheProfile.ToolCatalogJson = JsonSerializer.Serialize(tools, CacheJsonOptions);
        }
        catch
        {
            // 缓存写入失败不应影响已成功获取的在线目录。
        }
    }

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;

    private static bool ToolSummariesEquivalent(ToolPackageSummary left, ToolPackageSummary right) =>
        JsonSerializer.Serialize(left, CacheJsonOptions) == JsonSerializer.Serialize(right, CacheJsonOptions);

    private static void ReconcileCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
        where T : class
    {
        for (var index = 0; index < desired.Count; index++)
        {
            var item = desired[index];
            var currentIndex = target.IndexOf(item);
            if (currentIndex < 0)
                target.Insert(index, item);
            else if (currentIndex != index)
                target.Move(currentIndex, index);
        }

        while (target.Count > desired.Count)
            target.RemoveAt(target.Count - 1);
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

    private void ClearToolDataMenuItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not MenuItem { CommandParameter: ToolCardViewModel card }) return;

        try
        {
            var size = ToolDataManager.GetToolDataSize(card.Id);
            if (size == 0 && !ToolDataManager.HasToolData(card.Id))
            {
                StatusText.Text = $"{card.Name} 当前没有已保存的数据。";
                return;
            }

            var sizeText = FormatDataSize(size);
            var result = PopupHelper.ShowConfirmDialog(
                $"确定清除“{card.Name}”的全部工具数据吗？\n\n当前占用：{sizeText}\n包括工具设置与上次窗口状态。此操作无法撤销；若工具仍在运行，关闭时可能重新写入窗口状态。",
                showCancelButton: true,
                confirmText: "清除数据");
            if (result != MessageBoxResult.OK) return;

            ToolDataManager.ClearToolData(card.Id);
            StatusText.Text = $"已清除 {card.Name} 的工具数据。";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"清除 {card.Name} 的数据失败：{exception.Message}";
        }
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

    private static string FormatDataSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024d / 1024:F1} MB";
        return $"{bytes / 1024d / 1024 / 1024:F1} GB";
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
            $"pack://application:,,,/{assemblyName};component/Resources/Image/default_tool_icon.png",
            UriKind.Absolute));
        image.Freeze();
        return image;
    }
}
