using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using XFEToolBox.Client.Utilities.Helpers;
using XFEToolBox.Client.Views.Controls;
using XFEToolBox.Client.Views.Pages;

namespace XFEToolBox.Client.ViewModel.Pages;

public partial class MainPageViewModel : ObservableObject
{
    private const int MaximumAttempts = 3;
    private Task? loadingTask;

    public MainPage MainPage { get; }

    public MainPageViewModel(MainPage mainPage)
    {
        MainPage = mainPage;
        MainPage.Loaded += MainPage_Loaded;
    }

    private async void MainPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!MainPage.mainCarousel.HasItems)
            await ReloadAsync();
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

    private sealed record DownloadedCover(BilibiliVideoInfo Video, byte[] ImageBytes);
}
