using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using XFEToolBox.Client.Utilities.Helpers;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Client.Models.Server;
using XFEToolBox.Client.Models;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Views.Controls;
using XFEToolBox.Client.Views.Pages;
using XFEToolBox.Client.Views.Windows;

namespace XFEToolBox.Client.ViewModel.Pages;

public partial class MainPageViewModel : ObservableObject
{
    private const int MaximumAttempts = 3;
    private const int CoverDownloadAttempts = 2;
    private const int PopularVideoCount = 3;
    private const int LatestVideoCount = 2;
    private const int TutorialVideoCount = 2;
    private const int ExpectedCarouselItemCount = PopularVideoCount + LatestVideoCount + TutorialVideoCount;
    private const int MaximumVisibleRecentItems = 8;
    private Task? loadingTask;
    private readonly DispatcherTimer adminRefreshTimer;
    private bool isAdminOverviewLoading;
    private bool hasAdminOverviewSnapshot;

    public MainPage MainPage { get; }

    public MainPageViewModel(MainPage mainPage)
    {
        MainPage = mainPage;
        MainPage.Loaded += MainPage_Loaded;
        MainPage.Unloaded += MainPage_Unloaded;
        ClientSession.SessionChanged += ClientSession_SessionChanged;
        RecentUsageService.Changed += RecentUsageService_Changed;
        adminRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, MainPage.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        adminRefreshTimer.Tick += AdminRefreshTimer_Tick;
    }

    [ObservableProperty] private Visibility adminServerPanelVisibility = Visibility.Collapsed;
    [ObservableProperty] private string adminServerStatus = "XFEToolBoxServer";
    [ObservableProperty] private string adminCpuText = "--";
    [ObservableProperty] private string adminCpuDetail = "--";
    [ObservableProperty] private string adminMemoryText = "--";
    [ObservableProperty] private string adminMemoryDetail = "--";
    [ObservableProperty] private string adminUsersText = "--";
    [ObservableProperty] private string adminUsersDetail = "--";
    [ObservableProperty] private string adminContentText = "--";
    [ObservableProperty] private string adminContentDetail = "--";
    [ObservableProperty] private string adminStorageText = "--";
    [ObservableProperty] private string adminStorageDetail = "--";
    [ObservableProperty] private string adminUptimeText = "--";
    [ObservableProperty] private string adminUptimeDetail = "--";
    [ObservableProperty] private string adminUpdatedText = string.Empty;
    [ObservableProperty] private Visibility recentItemsVisibility = Visibility.Collapsed;
    [ObservableProperty] private Visibility recentEmptyVisibility = Visibility.Visible;
    [ObservableProperty] private string recentUsageCountText = "0 项";
    public ObservableCollection<RecentUsageCardViewModel> RecentItems { get; } = [];

    private async void MainPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        RefreshRecentUsage();
        var tasks = new List<Task> { LoadAdminOverviewAsync() };
        if (!MainPage.mainCarousel.HasItems) tasks.Add(ReloadAsync());
        await Task.WhenAll(tasks);
        if (ClientSession.IsAdministrator) adminRefreshTimer.Start();
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e) => adminRefreshTimer.Stop();

    private async void AdminRefreshTimer_Tick(object? sender, EventArgs e) => await LoadAdminOverviewAsync();

    private void ClientSession_SessionChanged(object? sender, EventArgs e) => MainPage.Dispatcher.InvokeAsync(async () =>
    {
        RefreshRecentUsage();
        await LoadAdminOverviewAsync();
        if (ClientSession.IsAdministrator && MainPage.IsVisible) adminRefreshTimer.Start();
        else adminRefreshTimer.Stop();
    });

    private void RecentUsageService_Changed(object? sender, EventArgs e) =>
        MainPage.Dispatcher.InvokeAsync(RefreshRecentUsage);

    public async Task OpenRecentItemAsync(RecentUsageCardViewModel card)
    {
        if (!card.IsEnabled || MainWindow.Current is null) return;
        card.IsEnabled = false;
        try
        {
            switch (card.Entry.Kind)
            {
                case RecentUsageKind.Tool:
                    MainWindow.Current.ViewModel.NavigateToPageCommand.Execute("tool");
                    await ToolBoxPage.Current.OpenToolByIdAsync(card.Entry.TargetId);
                    break;

                case RecentUsageKind.Software:
                    MainWindow.Current.ViewModel.NavigateToPageCommand.Execute("download");
                    await DownloadPage.Current.OpenSoftwareByIdAsync(card.Entry.TargetId);
                    break;
            }
        }
        finally
        {
            card.IsEnabled = true;
        }
    }

    public void RemoveRecentItem(RecentUsageCardViewModel card) =>
        RecentUsageService.Remove(card.Entry.Kind, card.Entry.TargetId);

    public void ClearRecentUsage() => RecentUsageService.Clear();

    public void OpenToolBox() => MainWindow.Current?.ViewModel.NavigateToPageCommand.Execute("tool");

    private void RefreshRecentUsage()
    {
        var recent = RecentUsageService.GetRecent()
            .Where(entry => entry.Kind is RecentUsageKind.Tool or RecentUsageKind.Software)
            .Take(MaximumVisibleRecentItems)
            .Select(entry => new RecentUsageCardViewModel(entry))
            .ToArray();

        RecentItems.Clear();
        foreach (var item in recent) RecentItems.Add(item);

        RecentItemsVisibility = recent.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentEmptyVisibility = recent.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentUsageCountText = $"{recent.Length} 项";
    }

    private async Task LoadAdminOverviewAsync()
    {
        AdminServerPanelVisibility = ClientSession.IsAdministrator ? Visibility.Visible : Visibility.Collapsed;
        if (!ClientSession.IsAdministrator || isAdminOverviewLoading) return;

        isAdminOverviewLoading = true;
        try
        {
            var response = await ClientSession.Requester.Request<AdminOverview>("adminOverview");
            if (response.StatusCode != HttpStatusCode.OK || response.Result is null)
            {
                if (!hasAdminOverviewSnapshot)
                    AdminServerStatus = string.IsNullOrWhiteSpace(response.Message) ? "服务器状态不可用" : response.Message;
                return;
            }

            var overview = response.Result;
            AdminServerStatus = overview.Status == "running" ? $"{overview.ServerName} · 运行中" : overview.Status;
            AdminCpuText = $"{overview.CpuUsagePercent:F1}%";
            AdminCpuDetail = $"{overview.ProcessorCount} 个逻辑核心";
            AdminMemoryText = $"{overview.MemoryUsagePercent:F1}%";
            AdminMemoryDetail = $"已用 {FormatBytes(overview.UsedMemoryBytes)} / {FormatBytes(overview.TotalMemoryBytes)}\n进程 {FormatBytes(overview.WorkingSetBytes)} · 可用 {FormatBytes(overview.AvailableMemoryBytes)}";
            AdminUsersText = $"{overview.UserCount} 个用户";
            AdminUsersDetail = $"{overview.ActiveSessionCount} 个有效登录会话";
            AdminContentText = $"{overview.PublishedPackageCount} 工具 · {overview.PublishedSoftwareCount} 软件";
            AdminContentDetail = $"共 {overview.PackageCount} 个工具版本 · {overview.SoftwareCount} 个软件";
            AdminStorageText = FormatBytes(overview.StorageBytes);
            AdminStorageDetail = $"软件文件占用 {FormatBytes(overview.SoftwareStorageBytes)}";
            AdminUptimeText = FormatDuration(overview.UptimeSeconds);
            AdminUptimeDetail = $"服务器时间 {overview.Utc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
            AdminUpdatedText = $"运行 {FormatDuration(overview.UptimeSeconds)} · {overview.Utc.ToLocalTime():HH:mm:ss} 更新";
            hasAdminOverviewSnapshot = true;
        }
        catch (Exception exception)
        {
            if (!hasAdminOverviewSnapshot) AdminServerStatus = $"读取失败：{exception.Message}";
        }
        finally { isAdminOverviewLoading = false; }
    }

    public Task ReloadAsync()
    {
        if (loadingTask is { IsCompleted: false })
            return loadingTask;

        loadingTask = LoadCarouselAsync();
        return loadingTask;
    }

    private async Task LoadCarouselAsync()
    {
        var carousel = MainPage.mainCarousel;
        carousel.IsLoading = true;
        carousel.CanRetry = false;
        carousel.StatusMessage = "正在获取 B 站热门与最新视频…";

        try
        {
            var groups = await Task.WhenAll(
                LoadVideoGroupAsync(
                    "B站热门",
                    PopularVideoCount,
                    () => BilibiliHelper.GetPopularVideoListAsync(8)),
                LoadVideoGroupAsync(
                    "我的最新",
                    LatestVideoCount,
                    () => BilibiliHelper.GetLatestCreatorVideoListAsync(8)),
                LoadVideoGroupAsync(
                    "芝士 C#",
                    TutorialVideoCount,
                    () => BilibiliHelper.GetSeasonVideoListAsync(pageSize: 10)));

            foreach (var group in groups.Where(group => group.Videos.Count < group.RequiredCount))
            {
                Debug.WriteLine(
                    $"轮播分组“{group.Badge}”仅获取到 {group.Videos.Count}/{group.RequiredCount} 项：{group.LastException?.Message}");
            }

            var candidates = ComposeOrderedVideos(groups);
            if (candidates.Count == 0)
                throw new HttpRequestException("Bilibili 内容源暂时没有返回可展示的视频");

            var downloadedCovers = await Task.WhenAll(candidates.Select(DownloadCoverAsync));
            var carouselItems = new List<CarouselImageItem>(downloadedCovers.Length);

            foreach (var downloadedCover in downloadedCovers.OfType<DownloadedCover>())
            {
                var image = CreateBitmapImage(downloadedCover.ImageBytes);
                var video = downloadedCover.Candidate.Video;
                var videoUrl = $"https://www.bilibili.com/video/{video.Bvid}";
                carouselItems.Add(new CarouselImageItem
                {
                    Image = image,
                    Title = video.Title,
                    Badge = downloadedCover.Candidate.Badge,
                    Action = () => OpenExternalLink(videoUrl)
                });
            }

            if (carouselItems.Count == 0)
                throw new HttpRequestException("轮播封面图片全部下载失败");

            carousel.SetItems(carouselItems);
            carousel.StatusMessage = carouselItems.Count == ExpectedCarouselItemCount
                ? string.Empty
                : $"已加载 {carouselItems.Count}/{ExpectedCarouselItemCount} 项内容";
            carousel.CanRetry = carouselItems.Count < ExpectedCarouselItemCount;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"主页轮播内容加载失败：{exception}");
            carousel.StatusMessage = "网络连接失败，请稍后重新加载";
            carousel.CanRetry = true;
        }
        finally
        {
            carousel.IsLoading = false;
        }
    }

    private static async Task<VideoGroupResult> LoadVideoGroupAsync(
        string badge,
        int requiredCount,
        Func<Task<IReadOnlyList<BilibiliVideoInfo>>> loadAsync)
    {
        IReadOnlyList<BilibiliVideoInfo> bestResult = [];
        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                var videos = await loadAsync();
                if (videos.Count > bestResult.Count)
                    bestResult = videos;
                if (bestResult.Count >= requiredCount)
                    break;

                lastException = new HttpRequestException(
                    $"内容源仅返回 {bestResult.Count}/{requiredCount} 项视频");
            }
            catch (Exception exception)
            {
                lastException = exception;
            }

            if (attempt < MaximumAttempts)
                await Task.Delay(TimeSpan.FromMilliseconds(750 * attempt));
        }

        return new VideoGroupResult(badge, requiredCount, bestResult, lastException);
    }

    private static IReadOnlyList<CarouselVideoCandidate> ComposeOrderedVideos(
        IEnumerable<VideoGroupResult> groups)
    {
        var result = new List<CarouselVideoCandidate>(ExpectedCarouselItemCount);
        var usedBvids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var addedCount = 0;
            foreach (var video in group.Videos)
            {
                if (!usedBvids.Add(video.Bvid))
                    continue;

                result.Add(new CarouselVideoCandidate(video, group.Badge));
                addedCount++;
                if (addedCount >= group.RequiredCount)
                    break;
            }
        }

        return result;
    }

    private static void OpenExternalLink(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private static async Task<DownloadedCover?> DownloadCoverAsync(CarouselVideoCandidate candidate)
    {
        for (var attempt = 1; attempt <= CoverDownloadAttempts; attempt++)
        {
            try
            {
                var imageBytes = await BilibiliHelper.GetImageBytesAsync(candidate.Video.PictureUrl);
                if (imageBytes.Length > 0)
                    return new DownloadedCover(candidate, imageBytes);
            }
            catch (Exception exception)
            {
                if (attempt == CoverDownloadAttempts)
                    Debug.WriteLine($"轮播封面下载失败（{candidate.Video.Bvid}）：{exception.Message}");
            }
        }

        return null;
    }

    private static BitmapImage CreateBitmapImage(byte[] imageBytes)
    {
        using var imageStream = new MemoryStream(imageBytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 1200;
        image.StreamSource = imageStream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):F1} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):F0} MB",
        >= 1024L => $"{bytes / 1024d:F0} KB",
        _ => $"{bytes} B"
    };

    private static string FormatDuration(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return value.TotalDays >= 1 ? $"{(int)value.TotalDays} 天 {value.Hours} 小时" : $"{value.Hours} 小时 {value.Minutes} 分";
    }

    private sealed record VideoGroupResult(
        string Badge,
        int RequiredCount,
        IReadOnlyList<BilibiliVideoInfo> Videos,
        Exception? LastException);

    private sealed record CarouselVideoCandidate(BilibiliVideoInfo Video, string Badge);

    private sealed record DownloadedCover(CarouselVideoCandidate Candidate, byte[] ImageBytes);
}
