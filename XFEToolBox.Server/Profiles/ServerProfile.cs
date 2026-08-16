using XFEExtension.NetCore.AutoConfig;

namespace XFEToolBox.Server.Profiles;

/// <summary>
/// 工具服务器配置。首次运行时由 AutoConfig 生成 XML 配置文件。
/// </summary>
public partial class ServerProfile : XFEProfile
{
    public const string DefaultInitialAdminPassword = "ChangeMe_123!";

    public ServerProfile() => DefaultProfileOperationMode = ProfileOperationMode.Xml;

    [ProfileProperty] private string _httpAddress = "http://localhost:3000/";

    [ProfileProperty] private string _httpsAddress = "https://localhost:3400/";

    [ProfileProperty] private string _storageRoot = "Data/ToolPackages";

    [ProfileProperty] private string _softwareStorageRoot = "Data/SoftwareFiles";

    [ProfileProperty] private string _adminApiKey = string.Empty;

    [ProfileProperty] private long _maxPackageBytes = 10 * 1024 * 1024;

    [ProfileProperty] private long _maxSoftwareBytes = 256 * 1024 * 1024;

    [ProfileProperty] private long _maxExpandedBytes = 30 * 1024 * 1024;

    [ProfileProperty] private int _maxFileCount = 256;

    [ProfileProperty] private double _maxCompressionRatio = 100;

    [ProfileProperty] private int _loginKeepDays = 30;

    [ProfileProperty] private string _initialAdminUserName = "admin";

    [ProfileProperty] private string _initialAdminPassword = DefaultInitialAdminPassword;

    [ProfileProperty] private bool _allowRegistration = true;
}
