namespace XFEToolBox.Core.Downloads;

/// <summary>
/// 软件的获取方式。Direct 由客户端保存文件，Browser 交给官方页面继续处理。
/// </summary>
public enum SoftwareDownloadMode
{
    Direct,
    Browser,
    Server
}

/// <summary>
/// 一个软件下载渠道。名称由管理员自由设置，例如“官网”“GitHub”“国内镜像”。
/// </summary>
public sealed class SoftwareDownloadChannel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "官网";
    public string Url { get; set; } = string.Empty;
    public SoftwareDownloadMode Mode { get; set; } = SoftwareDownloadMode.Browser;
    public string FileName { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>服务器内部保存路径；客户端只使用软件与渠道标识发起下载。</summary>
    public string StorageKey { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>
/// 由工具箱服务器维护并下发的软件信息。
/// </summary>
public sealed class SoftwareCatalogItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Version { get; set; } = "最新版";

    /// <summary>多渠道下载配置。旧版单地址字段仍保留，以兼容已有 AutoConfig 数据。</summary>
    public SoftwareDownloadChannel[] Channels { get; set; } = [];

    public string DownloadUrl { get; set; } = string.Empty;
    public string WebsiteUrl { get; set; } = string.Empty;

    /// <summary>可选的 HTTP(S) 或 data:image 图标地址。</summary>
    public string IconUrl { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    /// <summary>可选的十六进制 SHA-256；配置后客户端会在下载完成后校验。</summary>
    public string Sha256 { get; set; } = string.Empty;

    public string Notice { get; set; } = string.Empty;
    public string[] Tags { get; set; } = [];
    public SoftwareDownloadMode DownloadMode { get; set; }
    public bool Featured { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Published { get; set; } = true;

    public SoftwareDownloadChannel[] GetEffectiveChannels()
    {
        if (Channels.Length > 0)
            return Channels.OrderBy(channel => channel.SortOrder).ThenBy(channel => channel.Name).ToArray();

        if (string.IsNullOrWhiteSpace(DownloadUrl)) return [];
        return
        [
            new SoftwareDownloadChannel
            {
                Id = "default",
                Name = DownloadMode == SoftwareDownloadMode.Browser ? "官网" : "默认下载",
                Url = DownloadUrl,
                Mode = DownloadMode,
                FileName = FileName,
                Sha256 = Sha256
            }
        ];
    }
}

public sealed class SoftwareCatalogResponse
{
    public SoftwareCatalogItem[] Items { get; set; } = [];
    public string[] Categories { get; set; } = [];
}
