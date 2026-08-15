using System.Net;
using XFEToolBox.Core.Downloads;
using XFEToolBox.Server.Profiles.Data;
using XFEExtension.NetCore.ServerInteractive.Attributes;
using XFEExtension.NetCore.ServerInteractive.Implements.CoreService;

namespace XFEToolBox.Server.Services;

public partial class SoftwareCatalogService : ServerCoreStandardServiceBase
{
    [EntryPoint("v1/software/list")]
    public async Task GetSoftwareCatalogEntryPoint()
    {
        if (!TryGetString("search", out var search) || !TryGetString("category", out var category))
        {
            await CloseWithError("search 和 category 必须是字符串。", HttpStatusCode.BadRequest);
            return;
        }

        IEnumerable<SoftwareCatalogItem> filtered = MainDataProfile.SoftwareCatalog
            .Where(item => item.Enabled && IsWebAddress(item.DownloadUrl));

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
            .Where(item => item.Enabled && !string.IsNullOrWhiteSpace(item.Category))
            .Select(item => item.Category.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        await Close(new SoftwareCatalogResponse { Items = items, Categories = categories });
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

    private static bool IsWebAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
