using System.Windows.Controls;
using XFEToolBox.Client.ViewModel.Pages;

namespace XFEToolBox.Client.Views.Pages;

/// <summary>
/// MainPage.xaml 的交互逻辑
/// </summary>
public partial class MainPage : Page
{
    public static MainPage? Current { get; set; } = new();
    public MainPageViewModel ViewModel { get; set; }
    public MainPage()
    {
        Current = this;
        InitializeComponent();
        ViewModel = new(this);
        DataContext = ViewModel;
    }

    private async void MainCarousel_RetryRequested(object? sender, EventArgs e) =>
        await ViewModel.ReloadAsync();
}
