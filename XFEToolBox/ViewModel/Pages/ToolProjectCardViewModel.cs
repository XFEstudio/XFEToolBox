using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using XFEToolBox.Client.Models;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Core.Tools;

namespace XFEToolBox.Client.ViewModel.Pages;

internal partial class ToolProjectCardViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ToolProjectHistoryItem project;

    public ToolProjectCardViewModel(ToolProjectHistoryItem project)
    {
        this.project = project;
        iconSource = CreateFallbackIcon();
        IconLoadingTask = LoadIconAsync();
    }

    internal Task IconLoadingTask { get; }
    public string Name => project.Name;
    public string ProjectPath => project.ProjectPath;
    public string StateText => project.StateText;
    public string LastOpenedText => project.LastOpenedText;
    public bool IsPinned => PinnedItemService.IsPinned(LauncherItemKind.Project, ProjectPath);
    public string PinText => IsPinned ? "取消固定" : "固定此项";

    [ObservableProperty]
    private ImageSource iconSource;

    public bool TogglePinned()
    {
        var success = IsPinned
            ? PinnedItemService.Unpin(LauncherItemKind.Project, ProjectPath)
            : PinnedItemService.TryPin(LauncherItemKind.Project, ProjectPath);
        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(PinText));
        return success;
    }

    private async Task LoadIconAsync()
    {
        if (!Directory.Exists(ProjectPath)) return;
        try
        {
            var manifestPath = Path.Combine(ProjectPath, "manifest.json");
            var manifest = JsonSerializer.Deserialize<ToolPackageManifest>(await File.ReadAllTextAsync(manifestPath), JsonOptions);
            if (string.IsNullOrWhiteSpace(manifest?.Icon)) return;

            var root = Path.GetFullPath(ProjectPath);
            var iconPath = Path.GetFullPath(Path.Combine(root, manifest.Icon));
            var relativePath = Path.GetRelativePath(root, iconPath);
            if (Path.IsPathRooted(relativePath)
                || relativePath.Equals("..", StringComparison.Ordinal)
                || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                return;

            var file = new FileInfo(iconPath);
            if (!file.Exists || file.Length == 0 || file.Length > WebImageSourceLoader.MaximumImageBytes) return;
            IconSource = await WebImageSourceLoader.DecodeAsync(await File.ReadAllBytesAsync(iconPath), sourceName: iconPath);
        }
        catch
        {
            // 清单或预览图标无效时保留内置默认工具图标。
        }
    }

    private static ImageSource CreateFallbackIcon()
    {
        var image = new BitmapImage(new Uri("/XFEToolBox;component/Resources/Image/default_tool_icon.png", UriKind.Relative));
        image.Freeze();
        return image;
    }
}
