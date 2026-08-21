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
using XFEToolBox.Client.Models;
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
    private readonly List<ToolCardViewModel> _tools = [];
    private ToolCatalogRowViewModel[] _toolRows = [];
    private int _visibleCategoryCount;
    private ToolPackageSummary[] _cachedCatalog = [];
    private bool _updatingCategories;
    private bool _cacheLoaded;
    private bool _hasCachedCatalog;
    private int _refreshGeneration;

    public static ToolBoxPage Current { get; private set; } = new();

    public ToolBoxPage()
    {
        Current = this;
        InitializeComponent();
        CategoryFilter.Items.Add("全部分类");
        CategoryFilter.SelectedIndex = 0;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync(refreshFullCatalog: true);
    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync(refreshFullCatalog: true);
    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync(updateCategories: false);

    private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await RefreshAsync(updateCategories: false);
    }

    private async void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingCategories && IsLoaded) await RefreshAsync(updateCategories: false);
    }

    private async Task RefreshAsync(bool updateCategories = true, bool refreshFullCatalog = false)
    {
        EnsureCacheLoaded();
        var query = SearchTextBox.Text.Trim();
        var category = GetSelectedCategory();
        if (_hasCachedCatalog)
        {
            if (updateCategories) UpdateCategories(GetCatalogCategories(_cachedCatalog), category);
            category = GetSelectedCategory();
            ApplyTools(FilterCachedTools(query, category));
            StatusText.Text = _tools.Count == 0
                ? "缓存中没有符合条件的工具，正在后台刷新…"
                : "已显示本地缓存，正在后台刷新…";
        }
        else
        {
            StatusText.Text = "正在连接工具服务器…";
        }

        var refreshGeneration = ++_refreshGeneration;
        SearchButton.IsEnabled = RefreshButton.IsEnabled = false;
        CategoryFilter.IsEnabled = false;
        try
        {
            var requestQuery = refreshFullCatalog ? string.Empty : query;
            var requestCategory = refreshFullCatalog ? string.Empty : category;
            var response = await ClientSession.Requester.Request<ToolPackageSummary[]>(
                "catalogTools", requestQuery, requestCategory);
            if (refreshGeneration != _refreshGeneration) return;
            if (response.StatusCode != HttpStatusCode.OK)
            {
                ShowRefreshFailure(response.Message);
                return;
            }

            var tools = response.Result ?? [];
            if (refreshFullCatalog || string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(category))
            {
                _cachedCatalog = tools;
                _hasCachedCatalog = true;
                TrySaveCatalogCache(tools);
                if (updateCategories)
                {
                    UpdateCategories(GetCatalogCategories(_cachedCatalog), category);
                    category = GetSelectedCategory();
                }
                tools = FilterCachedTools(query, category);
            }

            ApplyTools(tools);
            StatusText.Text = _tools.Count == 0
                ? "没有符合条件的工具。"
                : "点击卡片打开工具；右键卡片可打开工具菜单。";
        }
        catch (Exception exception)
        {
            if (refreshGeneration == _refreshGeneration)
                ShowRefreshFailure($"读取失败：{exception.Message}");
        }
        finally
        {
            if (refreshGeneration == _refreshGeneration)
            {
                SearchButton.IsEnabled = RefreshButton.IsEnabled = true;
                CategoryFilter.IsEnabled = true;
            }
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

    private ToolPackageSummary[] FilterCachedTools(string query, string category) =>
        _cachedCatalog.Where(tool =>
                (string.IsNullOrWhiteSpace(category)
                 || string.Equals(NormalizeCategory(tool.Category), category, StringComparison.CurrentCultureIgnoreCase))
                && (string.IsNullOrWhiteSpace(query)
                    || Contains(tool.Name, query)
                    || Contains(tool.Description, query)
                    || Contains(tool.Author, query)
                    || Contains(tool.Category, query)
                    || tool.Tags?.Any(tag => Contains(tag, query)) == true))
            .ToArray();

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

        _tools.Clear();
        _tools.AddRange(desiredCards);
        RebuildCatalogRows();
        EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ToolCountText.Text = FormatToolCount();
    }

    private void RebuildCatalogRows()
    {
        var categoryOrder = CategoryFilter.Items.Cast<object>()
            .Skip(1)
            .Select((item, index) => (Name: item?.ToString() ?? string.Empty, Index: index))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Min(item => item.Index), StringComparer.CurrentCultureIgnoreCase);

        var groups = _tools
            .GroupBy(card => NormalizeCategory(card.Category), StringComparer.CurrentCultureIgnoreCase)
            .Select(group => (Name: group.Key, Items: (IReadOnlyList<ToolCardViewModel>)group.ToArray()))
            .OrderBy(group => categoryOrder.TryGetValue(group.Name, out var index) ? index : int.MaxValue)
            .ThenBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var rows = new List<ToolCatalogRowViewModel>(_tools.Count / 2 + groups.Length * 2);
        foreach (var group in groups)
        {
            rows.Add(ToolCatalogRowViewModel.Header(
                new ToolCategoryGroupViewModel(group.Name, group.Items.Count)));
            for (var index = 0; index < group.Items.Count; index += 2)
                rows.Add(ToolCatalogRowViewModel.Cards(
                    group.Items[index],
                    index + 1 < group.Items.Count ? group.Items[index + 1] : null));
        }

        _visibleCategoryCount = groups.Length;
        _toolRows = rows.ToArray();
        ToolCards.ItemsSource = _toolRows;
    }

    private string FormatToolCount() => _tools.Count == 0
        ? "0 个工具"
        : $"{_tools.Count} 个工具 · {_visibleCategoryCount} 个分类";

    private static string NormalizeCategory(string? category) =>
        string.IsNullOrWhiteSpace(category) ? "其他工具" : category.Trim();

    private void ShowRefreshFailure(string message)
    {
        if (_hasCachedCatalog)
        {
            EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ToolCountText.Text = FormatToolCount();
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

    private static string[] GetCatalogCategories(IEnumerable<ToolPackageSummary> tools) =>
        tools.Select(tool => NormalizeCategory(tool.Category))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    private void UpdateCategories(IEnumerable<string> categories, string selectedCategory)
    {
        _updatingCategories = true;
        CategoryFilter.Items.Clear();
        CategoryFilter.Items.Add("全部分类");
        foreach (var category in categories) CategoryFilter.Items.Add(category);
        CategoryFilter.SelectedItem = CategoryFilter.Items.Cast<object>()
            .FirstOrDefault(item => string.Equals(item.ToString(), selectedCategory, StringComparison.CurrentCultureIgnoreCase));
        if (CategoryFilter.SelectedIndex < 0) CategoryFilter.SelectedIndex = 0;
        _updatingCategories = false;
    }

    private string GetSelectedCategory() =>
        CategoryFilter.SelectedIndex > 0 ? CategoryFilter.SelectedItem?.ToString() ?? string.Empty : string.Empty;

    private void ShowEmptyResult(string message)
    {
        _tools.Clear();
        _toolRows = [];
        _visibleCategoryCount = 0;
        ToolCards.ItemsSource = _toolRows;
        EmptyState.Visibility = Visibility.Visible;
        ToolCountText.Text = string.Empty;
        StatusText.Text = message;
    }

    private async void ToolCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: ToolCardViewModel card })
            await OpenToolAsync(card);
    }

    private async void OpenToolMenuItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is MenuItem { CommandParameter: ToolCardViewModel card })
            await OpenToolAsync(card);
    }

    /// <summary>
    /// 供主页最近使用卡片调用。优先使用本地目录快照，缺失时再刷新完整服务器目录。
    /// </summary>
    public async Task<bool> OpenToolByIdAsync(string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId)) return false;
        EnsureCacheLoaded();

        var card = _tools.FirstOrDefault(item => string.Equals(item.Id, toolId, StringComparison.OrdinalIgnoreCase));
        var summary = card?.Package ?? _cachedCatalog.FirstOrDefault(item =>
            string.Equals(item.Id, toolId, StringComparison.OrdinalIgnoreCase));
        if (summary is null)
        {
            try
            {
                StatusText.Text = "正在更新工具目录…";
                var response = await ClientSession.Requester.Request<ToolPackageSummary[]>(
                    "catalogTools", string.Empty, string.Empty);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    _cachedCatalog = response.Result ?? [];
                    _hasCachedCatalog = true;
                    TrySaveCatalogCache(_cachedCatalog);
                    summary = _cachedCatalog.FirstOrDefault(item =>
                        string.Equals(item.Id, toolId, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    StatusText.Text = string.IsNullOrWhiteSpace(response.Message)
                        ? "无法更新工具目录。"
                        : response.Message;
                }
            }
            catch (Exception exception)
            {
                StatusText.Text = $"更新工具目录失败：{exception.Message}";
            }
        }

        if (summary is null)
        {
            StatusText.Text = "该工具已下架或当前服务器不再提供。";
            return false;
        }

        card ??= new ToolCardViewModel(summary, CreateIconSource(summary.IconDataUrl), File.Exists(GetCachePath(summary)));
        return await OpenToolAsync(card);
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

    private async Task<bool> OpenToolAsync(ToolCardViewModel card)
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
                card.IsDownloading = true;
                card.IsDownloadIndeterminate = package.PackageSize <= 0;
                card.DownloadProgress = 0;
                card.DownloadProgressText = package.PackageSize > 0
                    ? $"0 B / {FormatDataSize(package.PackageSize)}"
                    : "正在连接下载服务器…";
                card.CacheState = "下载 0%";
                StatusText.Text = $"正在获取 {card.Name} {card.LatestVersion}…";
                var cacheDirectory = Path.GetDirectoryName(cachePath)!;
                Directory.CreateDirectory(cacheDirectory);
                temporaryPath = Path.Combine(cacheDirectory, $".{Guid.NewGuid():N}.download");

                using var client = new HttpClient
                {
                    BaseAddress = new Uri(ClientSession.ApiAddress + "/"),
                    Timeout = TimeSpan.FromMinutes(10)
                };
                var catalogClient = new ToolCatalogClient(client);
                var downloadProgress = new Progress<ToolPackageDownloadProgress>(item =>
                {
                    card.IsDownloadIndeterminate = item.TotalBytes is null or <= 0;
                    card.DownloadProgress = item.Percentage ?? 0;
                    card.DownloadProgressText = item.TotalBytes is > 0
                        ? $"{FormatDataSize(item.BytesReceived)} / {FormatDataSize(item.TotalBytes.Value)}"
                        : $"已下载 {FormatDataSize(item.BytesReceived)}";
                    card.CacheState = item.Percentage is { } percentage
                        ? $"下载 {percentage:0}%"
                        : "正在下载…";
                    StatusText.Text = $"正在下载 {card.Name} · {card.DownloadProgressText}";
                });
                await using (var output = new FileStream(
                                 temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await catalogClient.DownloadPackageAsync(package, output, downloadProgress);
                    await output.FlushAsync();
                }
                card.CacheState = "正在校验…";
                card.DownloadProgress = 100;
                card.IsDownloadIndeterminate = false;
                card.DownloadProgressText = "下载完成，校验通过";
                File.Move(temporaryPath, cachePath, overwrite: true);
                temporaryPath = null;
                card.IsDownloading = false;
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
            RecentUsageIconCache.Remember(RecentUsageKind.Tool, card.Id, card.IconSource);
            RecentUsageService.RecordTool(card.Package);
            return true;
        }
        catch (Exception exception)
        {
            card.CacheState = cachePath is not null && File.Exists(cachePath) ? "重试打开" : "重试获取";
            StatusText.Text = $"打开失败：{exception.Message}";
            return false;
        }
        finally
        {
            card.IsDownloading = false;
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
            var header = dataUrl[5..separator];
            var mediaType = header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            var bytes = Convert.FromBase64String(dataUrl[(separator + 1)..]);
            return WebImageSourceLoader.Decode(bytes, mediaType, "tool-icon");
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
