using System.Windows;
using System.Windows.Controls;
using XFEToolBox.Client.Utilities;
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

    private async void RecentItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: RecentUsageCardViewModel card })
            await ViewModel.OpenRecentItemAsync(card);
    }

    private void RemoveRecentItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { CommandParameter: RecentUsageCardViewModel card })
            ViewModel.RemoveRecentItem(card);
    }

    private void ClearRecentUsage_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.RecentItems.Count == 0) return;
        if (PopupHelper.ShowConfirmDialog("确定清空全部最近使用记录吗？", true, "清空记录") == MessageBoxResult.OK)
            ViewModel.ClearRecentUsage();
    }

    private void OpenToolBox_Click(object sender, RoutedEventArgs e) => ViewModel.OpenToolBox();
}
