using XFEToolBox.Core.Downloads;
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
EnsureInitialSoftwareCatalog();
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
        .AddService<SoftwareCatalogService>()
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

static void EnsureInitialSoftwareCatalog()
{
    if (MainDataProfile.SoftwareCatalog.Count > 0) return;

    MainDataProfile.SoftwareCatalog.Add(new SoftwareCatalogItem
    {
        Id = "steam",
        Name = "Steam",
        Summary = "游戏购买、下载、更新与社区服务平台。",
        Description = "Valve 推出的 PC 游戏平台，提供游戏商店、自动更新、云存档、创意工坊和社区功能。",
        Publisher = "Valve Corporation",
        Category = "游戏平台",
        Version = "最新版",
        DownloadUrl = "https://cdn.fastly.steamstatic.com/client/installer/SteamSetup.exe",
        WebsiteUrl = "https://store.steampowered.com/about/",
        FileName = "SteamSetup.exe",
        Tags = ["Steam", "游戏", "商店", "社区"],
        DownloadMode = SoftwareDownloadMode.Direct,
        Featured = true
    });
    MainDataProfile.SoftwareCatalog.Add(new SoftwareCatalogItem
    {
        Id = "watt-toolkit",
        Name = "Watt Toolkit",
        Summary = "开源、跨平台的多功能游戏工具箱。",
        Description = "Watt Toolkit（原 Steam++）提供网络加速、账号切换、库存游戏管理等功能。官方发行页会按系统提供当前版本。",
        Publisher = "BeyondDimension",
        Category = "游戏工具",
        Version = "最新版",
        DownloadUrl = "https://github.com/BeyondDimension/SteamTools/releases/latest",
        WebsiteUrl = "https://steampp.net/",
        Tags = ["Steam++", "Watt Toolkit", "网络加速", "开源"],
        DownloadMode = SoftwareDownloadMode.Browser,
        Featured = true
    });
    MainDataProfile.SoftwareCatalog.Add(new SoftwareCatalogItem
    {
        Id = "visual-studio",
        Name = "Visual Studio Community",
        Summary = "面向 .NET、C++ 与桌面开发的完整 IDE。",
        Description = "Microsoft Visual Studio Community 提供代码编辑、调试、测试、设计器和可扩展开发工具。官方页面会下发当前稳定版引导程序。",
        Publisher = "Microsoft",
        Category = "开发工具",
        Version = "最新版",
        DownloadUrl = "https://visualstudio.microsoft.com/downloads/",
        WebsiteUrl = "https://visualstudio.microsoft.com/",
        Tags = ["Visual Studio", ".NET", "C#", "C++", "IDE"],
        DownloadMode = SoftwareDownloadMode.Browser,
        Featured = true
    });
    MainDataProfile.SoftwareCatalog.Add(new SoftwareCatalogItem
    {
        Id = "cheat-engine",
        Name = "Cheat Engine",
        Summary = "用于学习和研究的开源内存扫描与调试工具。",
        Description = "Cheat Engine 提供内存扫描、调试和逆向研究功能。下载和使用前请确认遵守目标软件的许可协议与当地法律。",
        Publisher = "Cheat Engine Project",
        Category = "调试工具",
        Version = "最新版",
        DownloadUrl = "https://www.cheatengine.org/download.php",
        WebsiteUrl = "https://www.cheatengine.org/",
        Notice = "仅用于合法的软件调试、学习和研究；请勿违反第三方软件的 EULA 或服务条款。",
        Tags = ["内存", "调试", "逆向", "开源"],
        DownloadMode = SoftwareDownloadMode.Browser
    });
    MainDataProfile.SaveProfile();
    Console.WriteLine("[初始化] 已创建默认软件下载目录。可通过 MainDataProfile 配置维护。");
}

static void SaveProfiles()
{
    ServerProfile.SaveProfile();
    MainDataProfile.SaveProfile();
    UserDataProfile.SaveProfile();
}
