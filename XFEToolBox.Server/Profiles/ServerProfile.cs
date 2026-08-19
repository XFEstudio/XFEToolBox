using XFEExtension.NetCore.AutoConfig;

namespace XFEToolBox.Server.Profiles;

/// <summary>
/// 工具服务器配置。首次运行时由 AutoConfig 生成 XML 配置文件。
/// </summary>
public partial class ServerProfile : XFEProfile
{
    public const string DefaultInitialAdminPassword = "ChangeMe_123!";

    public ServerProfile() => DefaultProfileOperationMode = ProfileOperationMode.Xml;

    [ProfileProperty] public static string HttpAddress { get; set; } = "http://localhost:3000/";
    [ProfileProperty] public static string HttpsAddress { get; set; } = "https://localhost:3400/";
    [ProfileProperty] public static string StorageRoot { get; set; } = "Data/ToolPackages";
    [ProfileProperty] public static string SoftwareStorageRoot { get; set; } = "Data/SoftwareFiles";
    [ProfileProperty] public static string AdminApiKey { get; set; } = string.Empty;
    [ProfileProperty] public static long MaxPackageBytes { get; set; } = 10 * 1024 * 1024;
    [ProfileProperty] public static long MaxSoftwareBytes { get; set; } = 256 * 1024 * 1024;
    [ProfileProperty] public static long MaxExpandedBytes { get; set; } = 30 * 1024 * 1024;
    [ProfileProperty] public static int MaxFileCount { get; set; } = 256;
    [ProfileProperty] public static double MaxCompressionRatio { get; set; } = 100;
    [ProfileProperty] public static int LoginKeepDays { get; set; } = 30;
    [ProfileProperty] public static string InitialAdminUserName { get; set; } = "admin";
    [ProfileProperty] public static string InitialAdminPassword { get; set; } = DefaultInitialAdminPassword;
    [ProfileProperty] public static bool AllowRegistration { get; set; } = true;
}
