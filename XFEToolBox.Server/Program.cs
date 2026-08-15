using XFEToolBox.Core.Models.Users;
using XFEToolBox.Server.Core.Options;
using XFEToolBox.Server.Core.Services;
using XFEToolBox.Server.Profiles;
using XFEToolBox.Server.Profiles.Data;
using XFEToolBox.Server.Services;
using XFEExtension.NetCore.ServerInteractive.Interfaces;
using XFEExtension.NetCore.ServerInteractive.Utilities.Extensions;
using XFEExtension.NetCore.ServerInteractive.Utilities.Server;

EnsureInitialAdministrator();
AppDomain.CurrentDomain.ProcessExit += (_, _) => SaveProfiles();

var validationOptions = new ToolPackageValidationOptions
{
    MaxPackageBytes = ServerProfile.MaxPackageBytes,
    MaxExpandedBytes = ServerProfile.MaxExpandedBytes,
    MaxFileCount = ServerProfile.MaxFileCount,
    MaxCompressionRatio = ServerProfile.MaxCompressionRatio
};
var configuredStorageRoot = ServerProfile.StorageRoot;
var storageRoot = Path.GetFullPath(Path.IsPathRooted(configuredStorageRoot)
    ? configuredStorageRoot
    : Path.Combine(AppContext.BaseDirectory, configuredStorageRoot));
var adminApiKey = ServerProfile.AdminApiKey;
var packageRepository = new FileSystemToolPackageRepository(
    new ToolPackageValidator(validationOptions),
    validationOptions,
    new ToolPackageStorageOptions { StorageRoot = storageRoot });

var server = XFEServerBuilder.CreateBuilder()
    .UseXFEServer()
    .AddServerCore(XFEServerCoreBuilder.CreateBuilder()
        .AddParameter("ToolPackageRepository", packageRepository)
        .AddParameter("AdminApiKey", adminApiKey)
        .AddParameter("MaxPackageBytes", validationOptions.MaxPackageBytes)
        .AddService<HealthService>()
        .AddService<ToolCatalogService>()
        .AddService<ToolAdminService>()
        .AddService<UserProfileService>()
        .AddService<AdminManagementService>()
        .UseXFEStandardServerCore<ToolBoxUserFaceInfo>(options =>
        {
            options.GetUserFunction = static () => UserDataProfile.UserTable;
            options.AddUserFunction = static userInfo =>
            {
                if (userInfo is ToolBoxUser user)
                    UserDataProfile.UserTable.Add(user);
            };
            options.GetEncryptedUserLoginModelFunction = static () => UserDataProfile.LoginTable;
            options.AddEncryptedUserLoginModelFunction = UserDataProfile.LoginTable.Add;
            options.RemoveEncryptedUserLoginModelFunction = UserDataProfile.LoginTable.Remove;
            options.GetLoginKeepDays = static () => ServerProfile.LoginKeepDays;
            options.LoginResultConvertFunction = static user =>
                ToolBoxUserFaceInfo.FromUser((IUserInfo)user);
        })
        .Build(options =>
        {
            options.AcceptGet = true;
            options.AcceptPost = true;
            options.AcceptNonStandardJson = true;
            options.GetIPFunction = static args => args.RequestHeaders["X-Forwarded-For"] ?? args.ClientIP;
            options.BindIP(ServerProfile.HttpAddress);
            options.MainEntryPoint = "api";
            options.ServerCoreName = "XFEToolBoxServer";
        }))
    .Build();

Console.WriteLine("XFEToolBox Server");
Console.WriteLine($"  地址：{ServerProfile.HttpAddress.TrimEnd('/')}/api");
Console.WriteLine($"  数据：{storageRoot}");
Console.WriteLine($"  用户：{UserDataProfile.UserTable.Count}");
await server.Start();
return;

static void EnsureInitialAdministrator()
{
    if (UserDataProfile.UserTable.Any(user =>
            user.PermissionLevel >= (int)ToolBoxUserRole.Administrator)) return;

    var userName = ServerProfile.InitialAdminUserName;
    var password = ServerProfile.InitialAdminPassword;
    UserDataProfile.UserTable.Add(new ToolBoxUser
    {
        UserName = userName,
        Password = password,
        NickName = "工具箱管理员",
        Enable = true,
        Role = ToolBoxUserRole.Administrator
    });
    UserDataProfile.SaveProfile();
    Console.WriteLine($"[初始化] 已创建管理员账号：{userName}");
    if (password == ServerProfile.DefaultInitialAdminPassword)
        Console.WriteLine("[安全提示] 当前使用初始密码 ChangeMe_123!，请登录后立即修改。");
}

static void SaveProfiles()
{
    ServerProfile.SaveProfile();
    UserDataProfile.SaveProfile();
}
