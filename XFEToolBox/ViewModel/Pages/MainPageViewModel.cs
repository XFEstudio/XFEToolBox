using System.Diagnostics;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using XFEToolBox.Client.Utilities.Helpers;
using XFEToolBox.Client.Views.Pages;

namespace XFEToolBox.Client.ViewModel.Pages;

public partial class MainPageViewModel : ObservableObject
{
    public bool IsInitialized { get; set; } = false;
    public MainPage MainPage { get; set; }
    public MainPageViewModel(MainPage mainPage)
    {
        MainPage = mainPage;
        MainPage.Loaded += MainPage_Loaded;
    }

    private async void MainPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (IsInitialized)
            return;
        IsInitialized = true;
        int retryCount = 0;
        while (true)
        {
            try
            {
                var videoInfoList = await BilibiliHelper.GetSeasonVideoList();
                foreach (var videoInfo in videoInfoList)
                {
                    MainPage.mainCarousel.AddItem(new BitmapImage(new Uri(videoInfo["pic"].ToString())), videoInfo["title"].ToString(), () => Process.Start("explorer.exe", $"https://www.bilibili.com/video/{videoInfo["bvid"]}"));
                }
                break;
            }
            catch (Exception ex)
            {
                if (retryCount > 3)
                    break;
                await Task.Delay(3000);
                retryCount++;
            }
        }
    }
}
