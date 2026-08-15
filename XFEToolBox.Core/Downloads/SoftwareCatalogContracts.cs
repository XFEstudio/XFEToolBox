namespace XFEToolBox.Core.Downloads;

/// <summary>
/// 软件的获取方式。Direct 由客户端保存文件，Browser 交给官方页面继续处理。
/// </summary>
public enum SoftwareDownloadMode
{
    Direct,
    Browser
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
}

public sealed class SoftwareCatalogResponse
{
    public SoftwareCatalogItem[] Items { get; set; } = [];
    public string[] Categories { get; set; } = [];
}
