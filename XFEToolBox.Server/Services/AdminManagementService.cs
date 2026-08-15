using System.Net;
using XFEToolBox.Core.Models.Users;
using XFEToolBox.Server.Core.Exceptions;
using XFEToolBox.Server.Core.Services;
using XFEToolBox.Server.Core.Utilities;
using XFEToolBox.Server.Profiles.Data;
using XFEExtension.NetCore.ServerInteractive.Attributes;
using XFEExtension.NetCore.ServerInteractive.Utilities.Server.Services.CoreService;

namespace XFEToolBox.Server.Services;

public partial class AdminManagementService : ServerCoreUserServiceBase
{
    public IToolPackageRepository? ToolPackageRepository { get; set; }

    public long MaxPackageBytes { get; set; }

    [EntryPoint("v1/manage/overview")]
    public async Task GetOverviewEntryPoint()
    {
        if (!await VerifyAdministrator()) return;

        var packages = ToolPackageRepository is null
            ? []
            : await ToolPackageRepository.ListAsync(publishedOnly: false);
        await Close(new
        {
            serverName = "XFEToolBoxServer",
            status = "running",
            utc = DateTimeOffset.UtcNow,
            userCount = UserDataProfile.UserTable.Count,
            activeSessionCount = UserDataProfile.LoginTable.Count,
            packageCount = packages.Count,
            publishedPackageCount = packages.Count(package => package.Published),
            storageBytes = packages.Sum(package => package.PackageSize)
        });
    }

    [EntryPoint("v1/manage/users/list")]
    public async Task GetUsersEntryPoint()
    {
        if (!await VerifyAdministrator()) return;

        await Close(UserDataProfile.UserTable
            .OrderByDescending(user => user.PermissionLevel)
            .ThenBy(user => user.UserName, StringComparer.OrdinalIgnoreCase)
            .Select(user => new
            {
                user.Id,
                user.UserName,
                user.NickName,
                user.Enable,
                user.PermissionLevel,
                role = user.Role.ToString()
            })
            .ToArray());
    }

    [EntryPoint("v1/manage/users/create")]
    public async Task CreateUserEntryPoint()
    {
        if (!await VerifyAdministrator()) return;

        var userName = GetString("userName");
        var password = GetString("password", trim: false);
        var nickName = GetString("nickName") ?? userName;
        if (userName is null || password is null)
        {
            await CloseWithError("账号和密码不能为空。", HttpStatusCode.BadRequest);
            return;
        }

        if (userName.Length is < 3 or > 32 || password.Length is < 8 or > 128)
        {
            await CloseWithError("账号长度应为 3-32 个字符，密码长度应为 8-128 个字符。", HttpStatusCode.BadRequest);
            return;
        }

        if (UserDataProfile.UserTable.Any(user =>
                string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase)))
        {
            await CloseWithError("该账号已存在。", HttpStatusCode.Conflict);
            return;
        }

        var user = new ToolBoxUser
        {
            UserName = userName,
            Password = password,
            NickName = nickName ?? userName,
            Enable = true,
            Role = GetBoolean("isAdministrator") ? ToolBoxUserRole.Administrator : ToolBoxUserRole.User
        };
        UserDataProfile.UserTable.Add(user);
        UserDataProfile.SaveProfile();
        await Close(new { user.Id, user.UserName, user.NickName, user.Enable, user.PermissionLevel });
    }

    [EntryPoint("v1/manage/users/update")]
    public async Task UpdateUserEntryPoint()
    {
        if (!await VerifyAdministrator()) return;

        var id = GetString("id");
        var user = UserDataProfile.UserTable.FirstOrDefault(item => item.Id == id);
        if (user is null)
        {
            await CloseWithError("用户不存在。", HttpStatusCode.NotFound);
            return;
        }

        var enabled = GetNullableBoolean("enabled");
        var isAdministrator = GetNullableBoolean("isAdministrator");
        var nickName = GetString("nickName");
        var password = GetString("password", trim: false);

        if (user.Id == User.Id && (enabled == false || isAdministrator == false))
        {
            await CloseWithError("不能禁用当前账号或移除自己的管理员权限。", HttpStatusCode.BadRequest);
            return;
        }

        if (password is not null && password.Length is < 8 or > 128)
        {
            await CloseWithError("密码长度应为 8-128 个字符。", HttpStatusCode.BadRequest);
            return;
        }

        if (enabled.HasValue) user.Enable = enabled.Value;
        if (isAdministrator.HasValue)
            user.Role = isAdministrator.Value ? ToolBoxUserRole.Administrator : ToolBoxUserRole.User;
        if (nickName is not null) user.NickName = nickName;
        if (password is not null)
        {
            user.Password = password;
            foreach (var login in UserDataProfile.LoginTable
                         .Where(item => item.UserLoginModel.Uid == user.Id)
                         .ToArray())
                UserDataProfile.LoginTable.Remove(login);
        }

        UserDataProfile.SaveProfile();
        await Close(new { user.Id, user.UserName, user.NickName, user.Enable, user.PermissionLevel });
    }

    [EntryPoint("v1/manage/tools/list")]
    public async Task GetToolsEntryPoint()
    {
        if (!await VerifyAdministrator() || !await VerifyRepository()) return;
        var packages = await ToolPackageRepository!.ListAsync(publishedOnly: false);
        await Close(packages
            .OrderBy(package => package.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(package => package.Manifest.Version, SemanticVersionComparer.Instance)
            .Select(ToolPackageContractMapper.ToUploadResult)
            .ToArray());
    }

    [EntryPoint("v1/manage/tools/upload")]
    public async Task UploadToolEntryPoint()
    {
        if (!await VerifyAdministrator() || !await VerifyRepository()) return;
        var packageBase64 = GetString("packageBase64", trim: false);
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
            var package = await ToolPackageRepository!.SaveAsync(
                stream,
                GetNullableBoolean("published") ?? true,
                GetNullableBoolean("overwrite") ?? false);
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

    [EntryPoint("v1/manage/tools/publication")]
    public async Task SetPublicationEntryPoint()
    {
        if (!await VerifyAdministrator() || !await VerifyRepository()) return;
        var toolId = GetString("toolId");
        var version = GetString("version");
        var published = GetNullableBoolean("published");
        if (toolId is null || version is null || !published.HasValue)
        {
            await CloseWithError("toolId、version 和 published 均为必填项。", HttpStatusCode.BadRequest);
            return;
        }

        try
        {
            var package = await ToolPackageRepository!.SetPublishedAsync(toolId, version, published.Value);
            await Close(ToolPackageContractMapper.ToUploadResult(package));
        }
        catch (ToolPackageNotFoundException exception)
        {
            await CloseWithError(exception.Message, HttpStatusCode.NotFound);
        }
    }

    private async Task<bool> VerifyAdministrator()
    {
        if (User.PermissionLevel >= (int)ToolBoxUserRole.Administrator) return true;
        await CloseWithError("需要管理员权限。", HttpStatusCode.Forbidden);
        return false;
    }

    private async Task<bool> VerifyRepository()
    {
        if (ToolPackageRepository is not null) return true;
        await CloseWithError("工具包仓库未初始化。", HttpStatusCode.InternalServerError);
        return false;
    }

    private string? GetString(string propertyName, bool trim = true)
    {
        try
        {
            var value = Json?[propertyName]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value)) return null;
            return trim ? value.Trim() : value;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private bool GetBoolean(string propertyName) => GetNullableBoolean(propertyName) ?? false;

    private bool? GetNullableBoolean(string propertyName)
    {
        try
        {
            return Json?[propertyName]?.GetValue<bool>();
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
