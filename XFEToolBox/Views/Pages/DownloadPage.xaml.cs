using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Profiles.CacheProfiles;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Client.ViewModel.Pages;
using XFEToolBox.Core.Downloads;

namespace XFEToolBox.Client.Views.Pages;

public partial class DownloadPage : Page
{
    private static readonly HttpClient IconClient = CreateIconClient();
    private static readonly JsonSerializerOptions CacheJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ObservableCollection<SoftwareCardViewModel> _software = [];
    private SoftwareCatalogResponse _cachedCatalog = new();
    private bool _updatingCategories;
    private bool _cacheLoaded;
    private bool _hasCachedCatalog;
    private int _refreshGeneration;

    public static DownloadPage Current { get; private set; } = new();

    public DownloadPage()
    {
        Current = this;
        InitializeComponent();
        SoftwareCards.ItemsSource = _software;
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
            if (updateCategories) UpdateCategories(_cachedCatalog.Categories, category);
            category = GetSelectedCategory();
            ApplySoftware(FilterCachedSoftware(query, category));
            StatusText.Text = _software.Count == 0
                ? "缓存中没有符合条件的软件，正在后台刷新…"
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
            var response = await ClientSession.Requester.Request<SoftwareCatalogResponse>(
                "softwareCatalog", requestQuery, requestCategory);
            if (refreshGeneration != _refreshGeneration) return;
            if (response.StatusCode != HttpStatusCode.OK || response.Result is null)
            {
                ShowRefreshFailure(string.IsNullOrWhiteSpace(response.Message) ? "服务器没有返回软件下载目录。" : response.Message);
                return;
            }

            if (updateCategories)
            {
                UpdateCategories(response.Result.Categories, category);
                category = GetSelectedCategory();
            }
            IReadOnlyList<SoftwareCatalogItem> software = response.Result.Items;
            if (refreshFullCatalog || string.IsNullOrWhiteSpace(query) && string.IsNullOrWhiteSpace(category))
            {
                _cachedCatalog = response.Result;
                _hasCachedCatalog = true;
                TrySaveCatalogCache(response.Result);
                software = FilterCachedSoftware(query, category);
            }

            ApplySoftware(software);
            StatusText.Text = _software.Count == 0 ? "没有符合条件的软件。" : "点击卡片查看详情与获取方式。";
        }
        catch (Exception exception)
        {
            if (refreshGeneration == _refreshGeneration)
                ShowRefreshFailure($"连接失败：{exception.Message}");
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
            var json = AppCacheProfile.SoftwareCatalogJson;
            if (string.IsNullOrWhiteSpace(json)) return;
            _cachedCatalog = JsonSerializer.Deserialize<SoftwareCatalogResponse>(json, CacheJsonOptions) ?? new SoftwareCatalogResponse();
            _cachedCatalog.Items ??= [];
            _cachedCatalog.Categories ??= [];
            _hasCachedCatalog = true;
        }
        catch
        {
            _cachedCatalog = new SoftwareCatalogResponse();
            _hasCachedCatalog = false;
        }
    }

    private SoftwareCatalogItem[] FilterCachedSoftware(string query, string category) =>
        _cachedCatalog.Items.Where(item =>
                (string.IsNullOrWhiteSpace(category) || string.Equals(item.Category, category, StringComparison.CurrentCultureIgnoreCase))
                && (string.IsNullOrWhiteSpace(query)
                    || Contains(item.Name, query)
                    || Contains(item.Summary, query)
                    || Contains(item.Description, query)
                    || Contains(item.Publisher, query)
                    || Contains(item.Category, query)
                    || item.Tags?.Any(tag => Contains(tag, query)) == true))
            .ToArray();

    private void ApplySoftware(IReadOnlyList<SoftwareCatalogItem> software)
    {
        var desiredCards = software.Select(item =>
        {
            var existing = _software.FirstOrDefault(card =>
                string.Equals(card.Id, item.Id, StringComparison.OrdinalIgnoreCase)
                && SoftwareItemsEquivalent(card.Software, item));
            if (existing is not null) return existing;
            var card = new SoftwareCardViewModel(item, GetBundledIcon(item.Id));
            _ = LoadConfiguredIconAsync(card);
            return card;
        }).ToArray();

        ReconcileCollection(_software, desiredCards);
        EmptyState.Visibility = _software.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = "暂时没有找到软件";
        EmptyHint.Text = "可以换个关键词或分类，或稍后刷新再试";
        SoftwareCountText.Text = $"{_software.Count} 个软件";
    }

    private void ShowRefreshFailure(string message)
    {
        if (_hasCachedCatalog)
        {
            EmptyState.Visibility = _software.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SoftwareCountText.Text = $"{_software.Count} 个软件";
            StatusText.Text = $"后台刷新失败，当前显示本地缓存：{message}";
            return;
        }

        ShowEmptyResult(message, connectionError: true);
    }

    private static void TrySaveCatalogCache(SoftwareCatalogResponse catalog)
    {
        try
        {
            AppCacheProfile.SoftwareCatalogJson = JsonSerializer.Serialize(catalog, CacheJsonOptions);
        }
        catch
        {
            // 缓存写入失败不应影响已成功获取的在线目录。
        }
    }

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;

    private static bool SoftwareItemsEquivalent(SoftwareCatalogItem left, SoftwareCatalogItem right) =>
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

    private void ShowEmptyResult(string message, bool connectionError)
    {
        _software.Clear();
        EmptyState.Visibility = Visibility.Visible;
        EmptyTitle.Text = connectionError ? "无法读取软件下载目录" : "暂时没有找到软件";
        EmptyHint.Text = connectionError ? "请检查网络连接与服务状态，然后点击刷新" : "可以换个关键词或分类再试";
        SoftwareCountText.Text = string.Empty;
        StatusText.Text = message;
    }

    private void SoftwareCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: SoftwareCardViewModel card }) return;
        PopupHelper.ShowDialog(new DownloadInfoPage(card.Software, card.IconSource), new PopupWindowOptions
        {
            Title = card.Name,
            Subtitle = $"{card.Publisher} · {card.Category}",
            Width = 610,
            Height = 590,
            ContentMargin = new Thickness(0)
        });
    }

    private static async Task LoadConfiguredIconAsync(SoftwareCardViewModel card)
    {
        if (string.IsNullOrWhiteSpace(card.Software.IconUrl)) return;
        try
        {
            var icon = await ReadIconAsync(card.Software.IconUrl);
            if (icon is not null) card.IconSource = icon;
        }
        catch
        {
            // 无法读取服务端配置的图标时保留内置或默认图标。
        }
    }

    private static async Task<ImageSource?> ReadIconAsync(string value)
    {
        byte[] bytes;
        if (value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            var separator = value.IndexOf(',');
            if (separator < 0) return null;
            bytes = Convert.FromBase64String(value[(separator + 1)..]);
        }
        else
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return null;
            bytes = await IconClient.GetByteArrayAsync(uri);
        }

        if (bytes.Length == 0 || bytes.Length > 1024 * 1024) return null;
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static ImageSource GetBundledIcon(string id)
    {
        var resource = id.ToLowerInvariant() switch
        {
            "steam" => "/Resources/Image/DownloadImage/steam_logo.png",
            "watt-toolkit" or "steampp" => "/Resources/Image/DownloadImage/steampp.png",
            "visual-studio" => "/Resources/Image/DownloadImage/visual_studio.png",
            "cheat-engine" => "/Resources/Image/DownloadImage/cheat_engine.png",
            _ => "/Resources/Image/download.png"
        };
        var image = new BitmapImage(new Uri($"pack://application:,,,{resource}", UriKind.Absolute));
        image.Freeze();
        return image;
    }

    private static HttpClient CreateIconClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("XFEToolBox/0.2");
        return client;
    }
}
