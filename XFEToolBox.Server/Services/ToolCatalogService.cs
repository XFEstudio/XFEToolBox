using System.Net;
using XFEToolBox.Core.Tools;
using XFEToolBox.Server.Core.Models;
using XFEToolBox.Server.Core.Services;
using XFEToolBox.Server.Core.Utilities;
using XFEExtension.NetCore.ServerInteractive.Attributes;
using XFEExtension.NetCore.ServerInteractive.Implements.CoreService;

namespace XFEToolBox.Server.Services;

public partial class ToolCatalogService : ServerCoreStandardServiceBase
{
    public IToolPackageRepository? ToolPackageRepository { get; set; }

    [EntryPoint("v1/tools/list")]
    public async Task GetPublishedToolsEntryPoint()
    {
        if (ToolPackageRepository is null)
        {
            await CloseWithError("工具包仓库未初始化。", HttpStatusCode.InternalServerError);
            return;
        }

        string? search;
        string? category;
        try
        {
            search = Json?["search"]?.GetValue<string>();
            category = Json?["category"]?.GetValue<string>();
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            await CloseWithError("search 和 category 必须是字符串。", HttpStatusCode.BadRequest);
            return;
        }
        var packages = await ToolPackageRepository.ListAsync(publishedOnly: true);
        IEnumerable<StoredToolPackage> filtered = packages;

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(package =>
                package.Manifest.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                package.Manifest.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                package.Manifest.Tags.Any(tag => tag.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(category))
            filtered = filtered.Where(package => string.Equals(package.Manifest.Category, category, StringComparison.OrdinalIgnoreCase));

        var result = filtered
            .GroupBy(package => package.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(package => package.Manifest.Version, SemanticVersionComparer.Instance).First())
            .Select(ToolPackageContractMapper.ToSummary)
            .OrderBy(tool => tool.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        await Close(result);
    }

    [EntryPoint("v1/tools/get")]
    public async Task GetPublishedToolEntryPoint()
    {
        if (ToolPackageRepository is null)
        {
            await CloseWithError("工具包仓库未初始化。", HttpStatusCode.InternalServerError);
            return;
        }

        var toolId = GetString("toolId");
        if (toolId is null)
        {
            await CloseWithError("缺少 toolId。", HttpStatusCode.BadRequest);
            return;
        }

        var packages = (await ToolPackageRepository.ListAsync(publishedOnly: true))
            .Where(package => string.Equals(package.Manifest.Id, toolId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(package => package.Manifest.Version, SemanticVersionComparer.Instance)
            .ToArray();
        if (packages.Length == 0)
        {
            await CloseWithError("工具不存在。", HttpStatusCode.NotFound);
            return;
        }

        await Close(new ToolPackageDetails
        {
            Manifest = packages[0].Manifest,
            Versions = packages.Select(ToolPackageContractMapper.ToVersionInfo).ToArray()
        });
    }

    [EntryPoint("v1/tools/download")]
    public async Task DownloadPublishedToolEntryPoint()
    {
        if (ToolPackageRepository is null)
        {
            await CloseWithError("工具包仓库未初始化。", HttpStatusCode.InternalServerError);
            return;
        }

        var toolId = GetString("toolId");
        var version = GetString("version");
        if (toolId is null || version is null)
        {
            await CloseWithError("缺少 toolId 或 version。", HttpStatusCode.BadRequest);
            return;
        }

        var package = await ToolPackageRepository.FindFileAsync(toolId, version, publishedOnly: true);
        if (package is null)
        {
            await CloseWithError("工具或版本不存在。", HttpStatusCode.NotFound);
            return;
        }

        Args.Response.ContentType = "application/vnd.xfestudio.xfetool";
        Args.Response.ContentLength64 = package.Package.PackageSize;
        Args.Response.Headers["Content-Disposition"] =
            $"attachment; filename=\"{package.Package.Manifest.Id}-{package.Package.Manifest.Version}.xfetool\"";
        Args.Response.Headers["ETag"] = $"\"{package.Package.Sha256}\"";
        await using var stream = new FileStream(
            package.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await Close(stream);
    }

    private string? GetString(string propertyName)
    {
        try
        {
            var value = Json?[propertyName]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
