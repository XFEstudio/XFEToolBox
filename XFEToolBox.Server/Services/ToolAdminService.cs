using System.Net;
using System.Security.Cryptography;
using System.Text;
using XFEToolBox.Server.Core.Exceptions;
using XFEToolBox.Server.Core.Services;
using XFEToolBox.Server.Core.Utilities;
using XFEExtension.NetCore.ServerInteractive.Attributes;
using XFEExtension.NetCore.ServerInteractive.Implements.CoreService;

namespace XFEToolBox.Server.Services;

public partial class ToolAdminService : ServerCoreStandardServiceBase
{
    public IToolPackageRepository? ToolPackageRepository { get; set; }

    public string? AdminApiKey { get; set; }

    public long MaxPackageBytes { get; set; }

    [EntryPoint("v1/admin/tools/list")]
    public async Task GetAllToolsEntryPoint()
    {
        if (!await VerifyCommonRequest(requirePost: false)) return;
        var packages = await ToolPackageRepository!.ListAsync(publishedOnly: false);
        await Close(packages
            .OrderBy(package => package.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(package => package.Manifest.Version, SemanticVersionComparer.Instance)
            .Select(ToolPackageContractMapper.ToUploadResult)
            .ToArray());
    }

    [EntryPoint("v1/admin/tools/upload")]
    public async Task UploadToolEntryPoint()
    {
        if (!await VerifyCommonRequest(requirePost: true)) return;
        var packageBase64 = GetString("packageBase64");
        if (packageBase64 is null)
        {
            await CloseWithError("缺少 packageBase64。", HttpStatusCode.BadRequest);
            return;
        }

        if (packageBase64.Length > checked(MaxPackageBytes * 2))
        {
            await CloseWithError("工具包 Base64 内容超过限制。", HttpStatusCode.RequestEntityTooLarge);
            return;
        }

        byte[] packageBytes;
        try
        {
            packageBytes = Convert.FromBase64String(packageBase64);
        }
        catch (FormatException)
        {
            await CloseWithError("packageBase64 不是有效的 Base64。", HttpStatusCode.BadRequest);
            return;
        }

        if (packageBytes.LongLength > MaxPackageBytes)
        {
            await CloseWithError("工具包超过大小限制。", HttpStatusCode.RequestEntityTooLarge);
            return;
        }

        if (!TryGetBoolean("published", defaultValue: true, out var published) ||
            !TryGetBoolean("overwrite", defaultValue: false, out var overwrite))
        {
            await CloseWithError("published 和 overwrite 必须是布尔值。", HttpStatusCode.BadRequest);
            return;
        }

        try
        {
            using var stream = new MemoryStream(packageBytes, writable: false);
            var package = await ToolPackageRepository!.SaveAsync(
                stream,
                published,
                overwrite);
            Args.Response.StatusCode = (int)HttpStatusCode.Created;
            await Close(ToolPackageContractMapper.ToUploadResult(package));
        }
        catch (ToolPackageValidationException exception)
        {
            await CloseWithError(exception.Message, HttpStatusCode.BadRequest);
        }
        catch (ToolPackageConflictException exception)
        {
            await CloseWithError(exception.Message, HttpStatusCode.Conflict);
        }
    }

    [EntryPoint("v1/admin/tools/publication")]
    public async Task SetPublicationEntryPoint()
    {
        if (!await VerifyCommonRequest(requirePost: true)) return;
        var toolId = GetString("toolId");
        var version = GetString("version");
        if (toolId is null || version is null)
        {
            await CloseWithError("缺少 toolId 或 version。", HttpStatusCode.BadRequest);
            return;
        }

        if (!TryGetBoolean("published", defaultValue: true, out var published))
        {
            await CloseWithError("published 必须是布尔值。", HttpStatusCode.BadRequest);
            return;
        }

        try
        {
            var package = await ToolPackageRepository!.SetPublishedAsync(toolId, version, published);
            await Close(ToolPackageContractMapper.ToUploadResult(package));
        }
        catch (ToolPackageNotFoundException exception)
        {
            await CloseWithError(exception.Message, HttpStatusCode.NotFound);
        }
    }

    private async Task<bool> VerifyCommonRequest(bool requirePost)
    {
        if (ToolPackageRepository is null)
        {
            await CloseWithError("工具包仓库未初始化。", HttpStatusCode.InternalServerError);
            return false;
        }

        if (requirePost && !string.Equals(Args.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            await CloseWithError("此接口只接受 POST 请求。", HttpStatusCode.MethodNotAllowed);
            return false;
        }

        if (string.IsNullOrWhiteSpace(AdminApiKey))
        {
            await CloseWithError("管理接口未配置。", HttpStatusCode.ServiceUnavailable);
            return false;
        }

        var suppliedKey = Args.RequestHeaders["X-Admin-Key"];
        if (string.IsNullOrWhiteSpace(suppliedKey))
        {
            var authorization = Args.RequestHeaders["Authorization"];
            if (authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
                suppliedKey = authorization["Bearer ".Length..].Trim();
        }

        if (!FixedTimeEquals(AdminApiKey, suppliedKey))
        {
            await CloseWithError("管理员密钥错误。", HttpStatusCode.Unauthorized);
            return false;
        }

        return true;
    }

    private string? GetString(string propertyName)
    {
        try
        {
            var value = Json?[propertyName]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private bool TryGetBoolean(string propertyName, bool defaultValue, out bool value)
    {
        try
        {
            value = Json?[propertyName]?.GetValue<bool>() ?? defaultValue;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            value = default;
            return false;
        }
    }

    private static bool FixedTimeEquals(string expected, string? actual)
    {
        if (actual is null) return false;
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }
}
