using XFEToolBox.Server.Core.Options;
using XFEToolBox.Server.Core.Services;
using XFEToolBox.Server.Profiles;
using XFEToolBox.Server.Services;
using XFEExtension.NetCore.ServerInteractive.Utilities.Extensions;
using XFEExtension.NetCore.ServerInteractive.Utilities.Server;

AppDomain.CurrentDomain.ProcessExit += (_, _) => ServerProfile.SaveProfile();

var validationOptions = new ToolPackageValidationOptions
{
    MaxPackageBytes = ServerProfile.MaxPackageBytes,
    MaxExpandedBytes = ServerProfile.MaxExpandedBytes,
    MaxFileCount = ServerProfile.MaxFileCount,
    MaxCompressionRatio = ServerProfile.MaxCompressionRatio
};
var configuredStorageRoot = Environment.GetEnvironmentVariable("XFETOOLBOX_STORAGE_ROOT")
                            ?? ServerProfile.StorageRoot;
var storageRoot = Path.GetFullPath(Path.IsPathRooted(configuredStorageRoot)
    ? configuredStorageRoot
    : Path.Combine(AppContext.BaseDirectory, configuredStorageRoot));
var adminApiKey = Environment.GetEnvironmentVariable("XFETOOLBOX_ADMIN_KEY")
                  ?? ServerProfile.AdminApiKey;
var packageRepository = new FileSystemToolPackageRepository(
    new ToolPackageValidator(validationOptions),
    validationOptions,
    new ToolPackageStorageOptions { StorageRoot = storageRoot });

if (string.IsNullOrWhiteSpace(adminApiKey))
    Console.WriteLine("[WARN]未配置管理员密钥，管理接口将返回 503。请设置 XFETOOLBOX_ADMIN_KEY。");

var server = XFEServerBuilder.CreateBuilder()
    .UseXFEServer()
    .AddServerCore(XFEServerCoreBuilder.CreateBuilder()
        .AddParameter("ToolPackageRepository", packageRepository)
        .AddParameter("AdminApiKey", adminApiKey)
        .AddParameter("MaxPackageBytes", validationOptions.MaxPackageBytes)
        .AddService<HealthService>()
        .AddService<ToolCatalogService>()
        .AddService<ToolAdminService>()
        .Build(options =>
        {
            options.AcceptGet = true;
            options.AcceptPost = true;
            options.AcceptNonStandardJson = true;
            options.GetIPFunction = static args => args.RequestHeaders["X-Forwarded-For"] ?? args.ClientIP;
            options.BindIP(ServerProfile.ServerHttpAddress);
            options.MainEntryPoint = "api";
            options.ServerCoreName = "ToolServer";
        }))
    .Build();

Console.WriteLine($"XFEToolBox 工具服务器数据目录：{storageRoot}");
await server.Start();
