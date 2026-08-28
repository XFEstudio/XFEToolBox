using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Models;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.ViewModel.Pages;
using XFEToolBox.Client.Views.Windows;

namespace XFEToolBox.Client.Views.Pages.Popups;

public partial class PinnedItemsPopupPage : Page, IPopupPage
{
    private readonly ObservableCollection<LauncherItemViewModel> items = [];

    public PinnedItemsPopupPage()
    {
        InitializeComponent();
        PinnedList.ItemsSource = items;
    }

    public PopupWindow? PopupWindow { get; set; }

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void PinnedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = PinnedList.SelectedIndex;
        MoveUpButton.IsEnabled = index > 0;
        MoveDownButton.IsEnabled = index >= 0 && index < items.Count - 1;
        RemoveButton.IsEnabled = index >= 0;
    }

    private async void MoveUpButton_Click(object sender, RoutedEventArgs e) => await MoveSelectedAsync(-1);

    private async void MoveDownButton_Click(object sender, RoutedEventArgs e) => await MoveSelectedAsync(1);

    private async void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (PinnedList.SelectedItem is not LauncherItemViewModel item) return;
        PinnedItemService.Unpin(item.Item.Kind, item.Item.TargetId);
        await ReloadAsync();
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (PopupWindow is not null) await PopupWindow.CloseWithResultAsync(MessageBoxResult.OK);
    }

    private async Task MoveSelectedAsync(int offset)
    {
        if (PinnedList.SelectedItem is not LauncherItemViewModel item) return;
        var selectedIndex = PinnedList.SelectedIndex;
        if (!PinnedItemService.Move(item.Item.Kind, item.Item.TargetId, offset)) return;
        await ReloadAsync();
        PinnedList.SelectedIndex = Math.Clamp(selectedIndex + offset, 0, items.Count - 1);
    }

    private async Task ReloadAsync()
    {
        var all = (await LauncherService.GetAllItemsAsync()).ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
        items.Clear();
        foreach (var pinned in PinnedItemService.GetPinnedItems())
        {
            var key = PinnedItemService.CreateKey(pinned.Kind, pinned.TargetId);
            if (!all.TryGetValue(key, out var item))
            {
                item = new LauncherItem
                {
                    Kind = pinned.Kind,
                    TargetId = pinned.TargetId,
                    Title = "暂不可用的固定项",
                    Subtitle = pinned.TargetId,
                    Detail = "对应缓存或本地项目当前不可用，可以保留或移除此记录。",
                    IconReference = "/Resources/Image/wrench.png",
                    IsPinned = true,
                    ExecuteAsync = static () => Task.CompletedTask
                };
            }
            items.Add(new LauncherItemViewModel(item));
        }
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MoveUpButton.IsEnabled = false;
        MoveDownButton.IsEnabled = false;
        RemoveButton.IsEnabled = false;
    }
}
