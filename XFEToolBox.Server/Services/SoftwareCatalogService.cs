using System.Net;
using XFEToolBox.Core.Downloads;
using XFEToolBox.Server.Profiles.Data;
using XFEExtension.NetCore.ServerInteractive.Attributes;
using XFEExtension.NetCore.ServerInteractive.Implements.CoreService;

namespace XFEToolBox.Server.Services;

public partial class SoftwareCatalogService : ServerCoreStandardServiceBase
{
    public string SoftwareStorageRoot { get; set; } = string.Empty;

    [EntryPoint("v1/software/list")]
    public async Task GetSoftwareCatalogEntryPoint()
    {
        if (!TryGetString("search", out var search) || !TryGetString("category", out var category))
        {
            await CloseWithError("search 和 category 必须是字符串。", HttpStatusCode.BadRequest);
            return;
        }

        IEnumerable<SoftwareCatalogItem> filtered = MainDataProfile.SoftwareCatalog
            .Where(item => item.Enabled && item.Published)
            .Select(ToPublicItem)
            .Where(item => item.Channels.Length > 0);

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(item =>
                item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Summary.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Publisher.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Tags.Any(tag => tag.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(category))
            filtered = filtered.Where(item => string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase));

        var items = filtered
            .OrderByDescending(item => item.Featured)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var categories = MainDataProfile.SoftwareCatalog
            .Where(item => item.Enabled && item.Published && !string.IsNullOrWhiteSpace(item.Category))
            .Select(item => item.Category.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        await Close(new SoftwareCatalogResponse { Items = items, Categories = categories });
    }

    [EntryPoint("v1/software/download")]
    public async Task DownloadSoftwareEntryPoint()
    {
        var softwareId = GetRequiredString("softwareId");
        var channelId = GetRequiredString("channelId");
        var software = MainDataProfile.SoftwareCatalog.FirstOrDefault(item =>
            item.Enabled && item.Published && string.Equals(item.Id, softwareId, StringComparison.OrdinalIgnoreCase));
        var channel = software?.GetEffectiveChannels().FirstOrDefault(item =>
            item.Enabled && string.Equals(item.Id, channelId, StringComparison.OrdinalIgnoreCase));
        if (software is null || channel is null)
        {
            await CloseWithError("软件或下载渠道不存在。", HttpStatusCode.NotFound);
            return;
        }

        if (channel.Mode != SoftwareDownloadMode.Server || string.IsNullOrWhiteSpace(channel.StorageKey))
        {
            await CloseWithError("该渠道不是服务器文件。", HttpStatusCode.BadRequest);
            return;
        }

        var fullPath = ResolveStoragePath(channel.StorageKey);
        if (fullPath is null || !File.Exists(fullPath))
        {
            await CloseWithError("服务器文件不存在。", HttpStatusCode.NotFound);
            return;
        }

        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await Close(stream);
    }

    private SoftwareCatalogItem ToPublicItem(SoftwareCatalogItem source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Summary = source.Summary,
        Description = source.Description,
        Publisher = source.Publisher,
        Category = source.Category,
        Version = source.Version,
        WebsiteUrl = source.WebsiteUrl,
        IconUrl = source.IconUrl,
        Notice = source.Notice,
        Tags = source.Tags,
        Featured = source.Featured,
        Enabled = source.Enabled,
        Published = source.Published,
        Channels = source.GetEffectiveChannels()
            .Where(IsAvailableChannel)
            .Select(channel => new SoftwareDownloadChannel
            {
                Id = channel.Id,
                Name = channel.Name,
                Url = channel.Url,
                Mode = channel.Mode,
                FileName = channel.FileName,
                Sha256 = channel.Sha256,
                Enabled = channel.Enabled,
                SortOrder = channel.SortOrder
            })
            .ToArray()
    };

    private bool IsAvailableChannel(SoftwareDownloadChannel channel)
    {
        if (!channel.Enabled || string.IsNullOrWhiteSpace(channel.Name)) return false;
        return channel.Mode == SoftwareDownloadMode.Server
            ? ResolveStoragePath(channel.StorageKey) is { } path && File.Exists(path)
            : IsWebAddress(channel.Url);
    }

    private string? ResolveStoragePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(SoftwareStorageRoot) || string.IsNullOrWhiteSpace(storageKey)) return null;
        var root = Path.GetFullPath(SoftwareStorageRoot);
        var path = Path.GetFullPath(Path.Combine(root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    private bool TryGetString(string propertyName, out string? value)
    {
        try
        {
            value = Json?[propertyName]?.GetValue<string>()?.Trim();
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            value = null;
            return false;
        }
    }

    private string? GetRequiredString(string propertyName) =>
        TryGetString(propertyName, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static bool IsWebAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
