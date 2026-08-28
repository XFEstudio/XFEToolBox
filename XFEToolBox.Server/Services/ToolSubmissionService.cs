using System.Net;
using XFEToolBox.Server.Core.Exceptions;
using XFEToolBox.Server.Core.Services;
using XFEExtension.NetCore.ServerInteractive.Attributes;
using XFEExtension.NetCore.ServerInteractive.Utilities.Server.Services.CoreService;

namespace XFEToolBox.Server.Services;

public partial class ToolSubmissionService : ServerCoreUserServiceBase
{
    public IToolPackageRepository? ToolPackageRepository { get; set; }

    public long MaxPackageBytes { get; set; }

    [EntryPoint("v1/user/tools/submit")]
    public async Task SubmitToolEntryPoint()
    {
        if (ToolPackageRepository is null)
        {
            await CloseWithError("工具包仓库未初始化。", HttpStatusCode.InternalServerError);
            return;
        }

        var packageBase64 = GetPackageBase64();
        if (packageBase64 is null || packageBase64.Length > checked(MaxPackageBytes * 2))
        {
            await CloseWithError("工具包内容为空或超过限制。", HttpStatusCode.RequestEntityTooLarge);
            return;
        }

        byte[] packageBytes;
        try
        {
            packageBytes = Convert.FromBase64String(packageBase64);
        }
        catch (FormatException)
        {
            await CloseWithError("工具包 Base64 无效。", HttpStatusCode.BadRequest);
            return;
        }

        if (packageBytes.LongLength > MaxPackageBytes)
        {
            await CloseWithError("工具包超过大小限制。", HttpStatusCode.RequestEntityTooLarge);
            return;
        }

        try
        {
            using var stream = new MemoryStream(packageBytes, writable: false);
            var package = await ToolPackageRepository.SaveSubmissionAsync(
                stream,
                User.Id,
                User.UserName);
            ReturnArgs.StatusCode = HttpStatusCode.Created;
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

    private string? GetPackageBase64()
    {
        try
        {
            var value = Json?["packageBase64"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
