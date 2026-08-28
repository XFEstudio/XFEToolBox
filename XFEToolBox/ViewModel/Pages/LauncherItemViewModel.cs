using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using XFEToolBox.Client.Models;
using XFEToolBox.Client.Utilities;

namespace XFEToolBox.Client.ViewModel.Pages;

public partial class LauncherItemViewModel : ObservableObject
{
    public LauncherItemViewModel(LauncherItem item)
    {
        Item = item;
        iconSource = CreateFallbackIcon(item);
        IconLoadingTask = LoadIconAsync();
    }

    public LauncherItem Item { get; }
    internal Task IconLoadingTask { get; }
    public string Title => Item.Title;
    public string Subtitle => Item.Subtitle;
    public string Detail => Item.Detail;
    public string KindText => Item.KindText;
    public bool IsPinned => PinnedItemService.IsPinned(Item.Kind, Item.TargetId);
    public string PinText => IsPinned ? "取消固定" : "固定";
    public string PinGlyph => IsPinned ? "★" : "☆";

    [ObservableProperty] private ImageSource iconSource;
    [ObservableProperty] private bool isEnabled = true;

    public async Task ExecuteAsync()
    {
        IsEnabled = false;
        try { await Item.ExecuteAsync(); }
        finally { IsEnabled = true; }
    }

    public bool TogglePinned()
    {
        var success = IsPinned
            ? PinnedItemService.Unpin(Item.Kind, Item.TargetId)
            : PinnedItemService.TryPin(Item.Kind, Item.TargetId);
        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(PinText));
        OnPropertyChanged(nameof(PinGlyph));
        return success;
    }

    private async Task LoadIconAsync()
    {
        var reference = Item.IconReference;
        if (string.IsNullOrWhiteSpace(reference) || reference.StartsWith('/')) return;
        try
        {
            var loaded = await WebImageSourceLoader.LoadAsync(reference);
            if (loaded is not null) IconSource = loaded;
        }
        catch
        {
            // 保留对应入口类型的内置图标。
        }
    }

    private static ImageSource CreateFallbackIcon(LauncherItem item)
    {
        var reference = item.IconReference.StartsWith('/')
            ? item.IconReference
            : item.Kind switch
            {
                LauncherItemKind.Tool => "/Resources/Image/default_tool_icon.png",
                LauncherItemKind.Software => "/Resources/Image/download.png",
                LauncherItemKind.Project or LauncherItemKind.Command => "/Resources/Image/wrench.png",
                _ => "/Resources/Image/grid.png"
            };
        var image = new BitmapImage(new Uri(
            $"/XFEToolBox;component{reference}",
            UriKind.Relative));
        image.Freeze();
        return image;
    }
}
