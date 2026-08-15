using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Client.ViewModel.Pages;
using XFEToolBox.Core.Downloads;

namespace XFEToolBox.Client.Views.Pages;

public partial class DownloadPage : Page
{
    private static readonly HttpClient IconClient = CreateIconClient();
    private readonly ObservableCollection<SoftwareCardViewModel> _software = [];
    private bool _updatingCategories;
    private bool _isLoading;

    public static DownloadPage Current { get; private set; } = new();

    public DownloadPage()
    {
        Current = this;
        InitializeComponent();
        SoftwareCards.ItemsSource = _software;
        CategoryFilter.Items.Add("全部分类");
        CategoryFilter.SelectedIndex = 0;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync(updateCategories: false);

    private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await RefreshAsync(updateCategories: false);
    }

    private async void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingCategories && IsLoaded) await RefreshAsync(updateCategories: false);
    }

    private async Task RefreshAsync(bool updateCategories = true)
    {
        if (_isLoading) return;
        _isLoading = true;
        SearchButton.IsEnabled = RefreshButton.IsEnabled = false;
        CategoryFilter.IsEnabled = false;
        StatusText.Text = "正在连接工具服务器…";

        try
        {
            var category = CategoryFilter.SelectedIndex > 0 ? CategoryFilter.SelectedItem?.ToString() ?? string.Empty : string.Empty;
            var response = await ClientSession.Requester.Request<SoftwareCatalogResponse>(
                "softwareCatalog", SearchTextBox.Text.Trim(), category);
            if (response.StatusCode != HttpStatusCode.OK || response.Result is null)
            {
                ShowEmptyResult(string.IsNullOrWhiteSpace(response.Message) ? "服务器没有返回软件下载目录。" : response.Message, connectionError: true);
                return;
            }

            if (updateCategories) UpdateCategories(response.Result.Categories, category);
            _software.Clear();
            foreach (var item in response.Result.Items)
            {
                var card = new SoftwareCardViewModel(item, GetBundledIcon(item.Id));
                _software.Add(card);
                _ = LoadConfiguredIconAsync(card);
            }

            EmptyState.Visibility = _software.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyTitle.Text = "暂时没有找到软件";
            EmptyHint.Text = "可以换个关键词或分类，或稍后刷新再试";
            SoftwareCountText.Text = $"{_software.Count} 个软件";
            StatusText.Text = _software.Count == 0 ? "没有符合条件的软件。" : "点击卡片查看详情与获取方式。";
        }
        catch (Exception exception)
        {
            ShowEmptyResult($"连接失败：{exception.Message}", connectionError: true);
        }
        finally
        {
            _isLoading = false;
            SearchButton.IsEnabled = RefreshButton.IsEnabled = true;
            CategoryFilter.IsEnabled = true;
        }
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
