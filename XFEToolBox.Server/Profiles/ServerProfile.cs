using XFEExtension.NetCore.AutoConfig;

namespace XFEToolBox.Server.Profiles;

/// <summary>
/// 工具服务器配置。首次运行时由 AutoConfig 生成 XML 配置文件。
/// </summary>
public partial class ServerProfile : XFEProfile
{
    public ServerProfile() => DefaultProfileOperationMode = ProfileOperationMode.Xml;

    [ProfileProperty] private string _serverHttpAddress = "http://localhost:5058/";

    [ProfileProperty] private string _storageRoot = "Data/ToolPackages";

    [ProfileProperty] private string _adminApiKey = string.Empty;

    [ProfileProperty] private long _maxPackageBytes = 10 * 1024 * 1024;

    [ProfileProperty] private long _maxExpandedBytes = 30 * 1024 * 1024;

    [ProfileProperty] private int _maxFileCount = 256;

    [ProfileProperty] private double _maxCompressionRatio = 100;
}
