using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XFEToolBox.Utilities.Helpers;
using XFEToolBox.Views.Pages;

namespace XFEToolBox.ViewModel.Pages;

public partial class MainPageViewModel : ObservableObject
{
    public MainPage MainPage { get; set; }
    public MainPageViewModel(MainPage mainPage)
    {
        MainPage = mainPage;
        MainPage.Loaded += MainPage_Loaded;
    }

    private async void MainPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        var videoInfoList = await BilibiliHelper.GetSeasonVideoList();
        foreach (var videoInfo in videoInfoList)
        {
            MainPage.mainCarousel.AddItem(new BitmapImage(new Uri(videoInfo["pic"].ToString())), videoInfo["title"].ToString());
        }
    }
}