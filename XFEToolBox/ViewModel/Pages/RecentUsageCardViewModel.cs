using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using XFEToolBox.Client.Models;
using XFEToolBox.Client.Profiles.CacheProfiles;
using XFEToolBox.Client.Utilities;

namespace XFEToolBox.Client.ViewModel.Pages;

public partial class RecentUsageCardViewModel : ObservableObject
{
    private static readonly object CatalogIconSyncRoot = new();
    private static string? cachedToolCatalogJson;
    private static string? cachedSoftwareCatalogJson;
    private static IReadOnlyDictionary<string, string> cachedToolIcons =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private static IReadOnlyDictionary<string, string> cachedSoftwareIcons =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public RecentUsageCardViewModel(RecentUsageEntry entry)
    {
        Entry = entry;
        if (RecentUsageIconCache.TryGet(entry.Kind, entry.TargetId, out var cachedIcon))
        {
            IconSource = cachedIcon;
        }
        else
        {
            IconSource = CreateFallbackIcon(entry);
            _ = LoadConfiguredIconAsync(entry);
        }
    }

    public RecentUsageEntry Entry { get; }
    public string Name => Entry.Name;
    public string Description => Entry.Description;
    public string Detail => Entry.Detail;
    public string KindText => Entry.Kind switch
    {
        RecentUsageKind.Tool => "工具",
        RecentUsageKind.Software => "软件",
        _ => "功能"
    };

    public string LastUsedText => FormatLastUsed(Entry.LastUsedAtUtc);
    [ObservableProperty]
    private ImageSource iconSource;

    [ObservableProperty]
    private bool isEnabled = true;

    private async Task LoadConfiguredIconAsync(RecentUsageEntry entry)
    {
        try
        {
            var reference = string.IsNullOrWhiteSpace(entry.IconReference)
                ? await Task.Run(() => ResolveCatalogIcon(entry))
                : entry.IconReference;
            if (string.IsNullOrWhiteSpace(reference) || reference.StartsWith('/'))
                return;

            var image = await WebImageSourceLoader.LoadAsync(reference);
            if (image is not null)
            {
                RecentUsageIconCache.Remember(entry.Kind, entry.TargetId, image);
                IconSource = image;
            }
        }
        catch
        {
            // 网络或图标格式不可用时保留对应类型的内置图标。
        }
    }

    private static ImageSource CreateFallbackIcon(RecentUsageEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.IconReference) && entry.IconReference.StartsWith('/'))
            return CreateResourceImage(entry.IconReference);

        return CreateResourceImage(entry.Kind switch
        {
            RecentUsageKind.Tool => "/Resources/Image/default_tool_icon.png",
            RecentUsageKind.Software => GetBundledSoftwareIcon(entry.TargetId),
            _ => "/Resources/Image/grid.png"
        });
    }

    private static string ResolveCatalogIcon(RecentUsageEntry entry)
    {
        var toolCatalogJson = AppCacheProfile.ToolCatalogJson;
        var softwareCatalogJson = AppCacheProfile.SoftwareCatalogJson;
        lock (CatalogIconSyncRoot)
        {
            if (!ReferenceEquals(cachedToolCatalogJson, toolCatalogJson))
            {
                cachedToolCatalogJson = toolCatalogJson;
                cachedToolIcons = BuildIconIndex(toolCatalogJson, isToolCatalog: true);
            }

            if (!ReferenceEquals(cachedSoftwareCatalogJson, softwareCatalogJson))
            {
                cachedSoftwareCatalogJson = softwareCatalogJson;
                cachedSoftwareIcons = BuildIconIndex(softwareCatalogJson, isToolCatalog: false);
            }

            var icons = entry.Kind == RecentUsageKind.Tool ? cachedToolIcons : cachedSoftwareIcons;
            return icons.TryGetValue(entry.TargetId, out var iconReference) ? iconReference : string.Empty;
        }
    }

    private static IReadOnlyDictionary<string, string> BuildIconIndex(string? json, bool isToolCatalog)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var document = JsonDocument.Parse(json);
            var items = isToolCatalog
                ? document.RootElement
                : document.RootElement.TryGetProperty("items", out var softwareItems)
                    ? softwareItems
                    : default;
            if (items.ValueKind != JsonValueKind.Array)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var iconPropertyName = isToolCatalog ? "iconDataUrl" : "iconUrl";
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idProperty) ||
                    !item.TryGetProperty(iconPropertyName, out var iconProperty))
                    continue;

                var id = idProperty.GetString();
                var icon = iconProperty.GetString();
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(icon))
                    result[id] = icon;
            }
            return result;
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string GetBundledSoftwareIcon(string id) => id.ToLowerInvariant() switch
    {
        "steam" => "/Resources/Image/DownloadImage/steam_logo.png",
        "watt-toolkit" or "steampp" => "/Resources/Image/DownloadImage/steampp.png",
        "visual-studio" => "/Resources/Image/DownloadImage/visual_studio.png",
        "cheat-engine" => "/Resources/Image/DownloadImage/cheat_engine.png",
        _ => "/Resources/Image/download.png"
    };

    private static ImageSource CreateResourceImage(string resourcePath)
    {
        var image = new BitmapImage(new Uri($"pack://application:,,,{resourcePath}", UriKind.Absolute));
        image.Freeze();
        return image;
    }

    private static string FormatLastUsed(DateTimeOffset value)
    {
        var elapsed = DateTimeOffset.UtcNow - value;
        if (elapsed < TimeSpan.FromMinutes(1)) return "刚刚";
        if (elapsed < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)elapsed.TotalMinutes)} 分钟前";
        if (elapsed < TimeSpan.FromDays(1)) return $"{Math.Max(1, (int)elapsed.TotalHours)} 小时前";
        if (elapsed < TimeSpan.FromDays(7)) return $"{Math.Max(1, (int)elapsed.TotalDays)} 天前";
        return value.ToLocalTime().ToString("M 月 d 日");
    }
}
