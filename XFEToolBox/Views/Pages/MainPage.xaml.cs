using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.ViewModel.Pages;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Views.Pages.Popups;
using XFEToolBox.Client.Views.Windows;

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

    private void OpenCommandPalette_Click(object sender, RoutedEventArgs e) =>
        ViewModel.OpenCommandPalette(HomeSearchBox.Text);

    private void HomeSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ViewModel.OpenCommandPalette(HomeSearchBox.Text);
        e.Handled = true;
    }

    private async void LauncherItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: LauncherItemViewModel item })
            await ViewModel.ExecuteLauncherItemAsync(item);
    }

    private void TogglePin_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { CommandParameter: LauncherItemViewModel item }) return;
        if (!ViewModel.TogglePinned(item))
            PopupHelper.ShowConfirmDialog($"最多只能固定 {PinnedItemService.MaximumPinnedItems} 项。", confirmText: "知道了");
    }

    private void AdminServerPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        MainWindow.Current?.ViewModel.NavigateToPageCommand.Execute("serverOverview");

    private void ManagePinnedItems_Click(object sender, RoutedEventArgs e) =>
        PopupHelper.ShowDialog(new PinnedItemsPopupPage(), new PopupWindowOptions
        {
            Title = "快速访问",
            Subtitle = "管理主页固定项",
            Width = 560,
            Height = 520,
            ContentMargin = new Thickness(0)
        });
}
