using System.Windows.Controls;
using XFEToolBox.Client.ViewModel.Pages;
using DownloadPageViewModel = XFEToolBox.Client.ViewModel.Pages.DownloadPageViewModel;

namespace XFEToolBox.Client.Views.Pages;

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
    }
}
