using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using XFEToolBox.Client.Models;
using XFEToolBox.Client.Profiles.CacheProfiles;
using XFEToolBox.Core.Downloads;
using XFEToolBox.Core.Tools;

namespace XFEToolBox.Client.ViewModel.Pages;

public partial class RecentUsageCardViewModel : ObservableObject
{
    public RecentUsageCardViewModel(RecentUsageEntry entry)
    {
        Entry = entry;
        IconSource = CreateIconSource(entry);
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
    public ImageSource IconSource { get; }

    [ObservableProperty]
    private bool isEnabled = true;

    private static ImageSource CreateIconSource(RecentUsageEntry entry)
    {
        var reference = string.IsNullOrWhiteSpace(entry.IconReference)
            ? ResolveCatalogIcon(entry)
            : entry.IconReference;
        if (!string.IsNullOrWhiteSpace(reference))
        {
            try
            {
                if (reference.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    var separator = reference.IndexOf(',');
                    if (separator > 0)
                    {
                        using var stream = new MemoryStream(Convert.FromBase64String(reference[(separator + 1)..]));
                        var dataImage = new BitmapImage();
                        dataImage.BeginInit();
                        dataImage.CacheOption = BitmapCacheOption.OnLoad;
                        dataImage.DecodePixelWidth = 96;
                        dataImage.StreamSource = stream;
                        dataImage.EndInit();
                        dataImage.Freeze();
                        return dataImage;
                    }
                }
                else if (reference.StartsWith('/'))
                {
                    return CreateResourceImage(reference);
                }
            }
            catch
            {
                // 损坏或不再可用的图标回退到对应类型的内置图标。
            }
        }

        return CreateResourceImage(entry.Kind switch
        {
            RecentUsageKind.Tool => "/Resources/Image/default_tool_icon.png",
            RecentUsageKind.Software => GetBundledSoftwareIcon(entry.TargetId),
            _ => "/Resources/Image/grid.png"
        });
    }

    private static string ResolveCatalogIcon(RecentUsageEntry entry)
    {
        try
        {
            if (entry.Kind == RecentUsageKind.Tool && !string.IsNullOrWhiteSpace(AppCacheProfile.ToolCatalogJson))
            {
                var tools = JsonSerializer.Deserialize<ToolPackageSummary[]>(
                    AppCacheProfile.ToolCatalogJson,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                return tools?.FirstOrDefault(item =>
                    string.Equals(item.Id, entry.TargetId, StringComparison.OrdinalIgnoreCase))?.IconDataUrl ?? string.Empty;
            }

            if (entry.Kind == RecentUsageKind.Software && !string.IsNullOrWhiteSpace(AppCacheProfile.SoftwareCatalogJson))
            {
                var catalog = JsonSerializer.Deserialize<SoftwareCatalogResponse>(
                    AppCacheProfile.SoftwareCatalogJson,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                return catalog?.Items.FirstOrDefault(item =>
                    string.Equals(item.Id, entry.TargetId, StringComparison.OrdinalIgnoreCase))?.IconUrl ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // 缓存失效时使用对应类型的内置图标。
        }

        return string.Empty;
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
