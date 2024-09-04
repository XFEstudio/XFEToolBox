using System.Windows.Controls;
using XFEToolBox.ViewModel.Pages;

namespace XFEToolBox.Views.Pages;

/// <summary>
/// DownloadPage.xaml 的交互逻辑
/// </summary>
public partial class DownloadPage : Page
{
    public static DownloadPage? Current { get; set; } = new();
    public DownloadPageViewModel ViewModel { get; set; }
    public DownloadPage()
    {
        Current = this;
        DataContext = ViewModel = new(this);
        InitializeComponent();
        //Steam链接：https://cdn.akamai.steamstatic.com/client/installer/SteamSetup.exe
    }
}
