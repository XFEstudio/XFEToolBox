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
using XFEToolBox.Client.Views.Controls;
using XFEToolBox.Client.Views.Pages;

namespace XFEToolBox.Client.ViewModel.Pages;

public partial class MainPageViewModel : ObservableObject
{
    private const int MaximumAttempts = 3;
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

    private async void MainPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        var tasks = new List<Task> { LoadAdminOverviewAsync() };
        if (!MainPage.mainCarousel.HasItems) tasks.Add(ReloadAsync());
        await Task.WhenAll(tasks);
        if (ClientSession.IsAdministrator) adminRefreshTimer.Start();
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e) => adminRefreshTimer.Stop();

    private async void AdminRefreshTimer_Tick(object? sender, EventArgs e) => await LoadAdminOverviewAsync();

    private void ClientSession_SessionChanged(object? sender, EventArgs e) => MainPage.Dispatcher.InvokeAsync(async () =>
    {
        await LoadAdminOverviewAsync();
        if (ClientSession.IsAdministrator && MainPage.IsVisible) adminRefreshTimer.Start();
        else adminRefreshTimer.Stop();
    });

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
        carousel.StatusMessage = "正在获取精选内容…";

        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            try
            {
                var videoInfoList = await BilibiliHelper.GetSeasonVideoList();
                var downloadTasks = videoInfoList.Select(DownloadCoverAsync).ToArray();
                var downloadedCovers = await Task.WhenAll(downloadTasks);
                var carouselItems = new List<CarouselImageItem>(downloadedCovers.Length);

                foreach (var downloadedCover in downloadedCovers.OfType<DownloadedCover>())
                {
                    var image = CreateBitmapImage(downloadedCover.ImageBytes);
                    var videoUrl = $"https://www.bilibili.com/video/{downloadedCover.Video.Bvid}";
                    carouselItems.Add(new CarouselImageItem
                    {
                        Image = image,
                        Title = downloadedCover.Video.Title,
                        Action = () => OpenExternalLink(videoUrl)
                    });
                }

                if (videoInfoList.Count > 0 && carouselItems.Count == 0)
                    throw new HttpRequestException("封面图片全部下载失败");

                carousel.SetItems(carouselItems);
                carousel.StatusMessage = carouselItems.Count == 0
                    ? "内容源暂时没有返回可展示的项目"
                    : string.Empty;
                carousel.CanRetry = carouselItems.Count == 0;
                carousel.IsLoading = false;
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt < MaximumAttempts)
                {
                    carousel.StatusMessage = $"连接不稳定，正在重试（{attempt}/{MaximumAttempts - 1}）…";
                    await Task.Delay(TimeSpan.FromMilliseconds(1000 * attempt));
                }
            }
        }

        Debug.WriteLine($"主页轮播内容加载失败：{lastException}");
        carousel.StatusMessage = "网络连接失败，请稍后重新加载";
        carousel.CanRetry = true;
        carousel.IsLoading = false;
    }

    private static void OpenExternalLink(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private static async Task<DownloadedCover?> DownloadCoverAsync(BilibiliVideoInfo video)
    {
        try
        {
            var imageBytes = await BilibiliHelper.GetImageBytesAsync(video.PictureUrl);
            return imageBytes.Length == 0 ? null : new DownloadedCover(video, imageBytes);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"轮播封面下载失败（{video.Bvid}）：{ex.Message}");
            return null;
        }
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

    private sealed record DownloadedCover(BilibiliVideoInfo Video, byte[] ImageBytes);
}
