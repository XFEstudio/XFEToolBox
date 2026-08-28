using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Models;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Client.Views.Pages;
using XFEToolBox.Client.Views.Pages.Popups;
using XFEToolBox.Core.Downloads;

namespace XFEToolBox.Client.ViewModel.Pages;

public partial class DownloadInfoPageViewModel : ObservableObject
{
    private static readonly HttpClient DownloadClient = CreateDownloadClient();
    private readonly SoftwareCatalogItem _software;

    public DownloadInfoPageViewModel(DownloadInfoPage viewPage, SoftwareCatalogItem software, ImageSource iconSource)
    {
        ViewPage = viewPage;
        _software = software;
        IconSource = iconSource;
        Channels = software.GetEffectiveChannels().Where(channel => channel.Enabled).ToArray();
        selectedChannel = Channels.FirstOrDefault() ?? new SoftwareDownloadChannel { Name = "暂无可用渠道", Enabled = false };
        DownloadButtonName = GetDownloadButtonName(selectedChannel);
        CanDownload = selectedChannel.Enabled;
    }

    public DownloadInfoPage ViewPage { get; }
    public string AppTitle => _software.Name;
    public string Summary => _software.Summary;
    public string Description => _software.Description;
    public string Publisher => _software.Publisher;
    public string Category => _software.Category;
    public string Version => _software.Version;
    public IReadOnlyList<SoftwareDownloadChannel> Channels { get; }
    public string DownloadModeText => SelectedChannel.Mode switch
    {
        SoftwareDownloadMode.Direct => "客户端直接下载",
        SoftwareDownloadMode.Server => "工具服务器下载",
        _ => "浏览器获取"
    };
    public string SelectedChannelName => SelectedChannel.Name;
    public string Notice => _software.Notice;
    public Visibility NoticeVisibility => string.IsNullOrWhiteSpace(_software.Notice) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility WebsiteVisibility => IsWebAddress(_software.WebsiteUrl) ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty] private ImageSource iconSource;
    [ObservableProperty] private SoftwareDownloadChannel selectedChannel;
    [ObservableProperty] private string downloadButtonName;
    [ObservableProperty] private string statusText = "确认信息后即可开始获取。";
    [ObservableProperty] private double progressValue;
    [ObservableProperty] private bool isProgressIndeterminate;
    [ObservableProperty] private Visibility progressVisibility = Visibility.Collapsed;
    [ObservableProperty] private bool canDownload = true;

    partial void OnSelectedChannelChanged(SoftwareDownloadChannel value)
    {
        DownloadButtonName = GetDownloadButtonName(value);
        CanDownload = value.Enabled;
        StatusText = value.Enabled ? $"已选择“{value.Name}”，确认后即可开始获取。" : "该渠道当前不可用。";
        OnPropertyChanged(nameof(DownloadModeText));
        OnPropertyChanged(nameof(SelectedChannelName));
    }

    [RelayCommand]
    private void OpenWebsite()
    {
        if (IsWebAddress(_software.WebsiteUrl)) OpenAddress(_software.WebsiteUrl);
    }

    [RelayCommand]
    private async Task DownloadClick()
    {
        if (!CanDownload || !EnsureAgreementAccepted()) return;

        var channel = SelectedChannel;
        if (channel.Mode == SoftwareDownloadMode.Browser)
        {
            try
            {
                OpenAddress(channel.Url);
                StatusText = $"已在浏览器中打开“{channel.Name}”。";
            }
            catch (Exception exception)
            {
                StatusText = $"无法打开官方页面：{exception.Message}";
            }
            return;
        }

        await DownloadDirectAsync(channel);
    }

    private bool EnsureAgreementAccepted()
    {
        if (DownloadProfile.DownloadAgreementAccepted) return true;

        var result = PopupHelper.ShowDialog(new AgreementDialogPopupPage
        {
            Title = "下载协议同意书",
            Agreement = """
                        1. 下载来源

                        软件信息和获取地址由当前配置的工具箱服务器提供。对于跳转到第三方官方网站的内容，实际文件、许可协议和隐私条款由对应发布者负责。

                        2. 安全检查

                        下载前请确认发布者、文件来源与数字签名。服务器配置了 SHA-256 时，工具箱会校验文件完整性；该校验不替代杀毒软件、数字签名或人工审核。

                        3. 使用责任

                        请遵守相关法律法规、软件许可协议和服务条款。不得将下载的软件用于侵权、破坏系统或其他非法用途。

                        4. 自动运行

                        若设置中启用了“下载完成后自动运行”，文件下载完成后会由系统打开。你可以随时在选项设置中关闭该功能。

                        继续即表示你已阅读并理解以上内容。
                        """
        }, new PopupWindowOptions
        {
            Title = "下载协议",
            Subtitle = "首次下载前需要确认",
            Width = 520,
            Height = 520,
            ContentMargin = new Thickness(12, 0, 12, 12)
        });
        if (result != MessageBoxResult.Yes) return false;

        DownloadProfile.DownloadAgreementAccepted = true;
        DownloadProfile.SaveProfile();
        return true;
    }

    private async Task DownloadDirectAsync(SoftwareDownloadChannel channel)
    {
        using var activity = ActivityCenterService.Start(
            $"下载 {_software.Name}", XFEToolBox.Client.Models.ActivityKind.SoftwareDownload, canCancel: true);
        var cancellationToken = activity.CancellationToken;
        string? temporaryPath = null;
        CanDownload = false;
        ProgressVisibility = Visibility.Visible;
        IsProgressIndeterminate = true;
        ProgressValue = 0;
        DownloadButtonName = "正在下载…";
        StatusText = "正在连接下载服务器…";

        try
        {
            Directory.CreateDirectory(DownloadProfile.DownloadDirectory);
            using var response = channel.Mode == SoftwareDownloadMode.Server
                ? await DownloadFromServerAsync(channel, cancellationToken)
                : IsWebAddress(channel.Url)
                    ? await DownloadClient.GetAsync(channel.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    : throw new InvalidOperationException("服务器返回了无效的下载地址。");
            response.EnsureSuccessStatusCode();

            var fileName = ResolveFileName(response, _software.Id, channel);
            var destinationPath = GetAvailablePath(Path.Combine(DownloadProfile.DownloadDirectory, fileName));
            temporaryPath = destinationPath + $".{Guid.NewGuid():N}.download";
            var contentLength = response.Content.Headers.ContentLength;
            IsProgressIndeterminate = contentLength is null or <= 0;

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    if (contentLength is > 0)
                    {
                        ProgressValue = received * 100d / contentLength.Value;
                        StatusText = $"正在下载… {FormatBytes(received)} / {FormatBytes(contentLength.Value)}";
                        activity.Report(ProgressValue, StatusText);
                    }
                    else
                    {
                        StatusText = $"正在下载… 已接收 {FormatBytes(received)}";
                        activity.Report(null, StatusText);
                    }
                }
                await output.FlushAsync(cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(channel.Sha256))
            {
                StatusText = "正在校验文件完整性…";
                await VerifySha256Async(temporaryPath, channel.Sha256, cancellationToken);
            }

            File.Move(temporaryPath, destinationPath);
            temporaryPath = null;
            ProgressValue = 100;
            IsProgressIndeterminate = false;
            StatusText = $"下载完成：{Path.GetFileName(destinationPath)}";
            activity.Succeed(StatusText);

            var followUpMessages = new List<string>();
            if (DownloadProfile.AutoRunWhenComplete)
            {
                try { Process.Start(new ProcessStartInfo(destinationPath) { UseShellExecute = true }); }
                catch (Exception exception) { followUpMessages.Add($"自动打开失败：{exception.Message}"); }
            }
            if (DownloadProfile.OpenFolderWhenComplete)
            {
                try { Process.Start(new ProcessStartInfo(DownloadProfile.DownloadDirectory) { UseShellExecute = true }); }
                catch (Exception exception) { followUpMessages.Add($"打开目录失败：{exception.Message}"); }
            }
            if (followUpMessages.Count > 0) StatusText += $"（{string.Join("；", followUpMessages)}）";
        }
        catch (OperationCanceledException)
        {
            ProgressValue = 0;
            IsProgressIndeterminate = false;
            StatusText = "下载已取消。";
            activity.Cancel(StatusText);
        }
        catch (Exception exception)
        {
            ProgressValue = 0;
            IsProgressIndeterminate = false;
            StatusText = $"下载失败：{exception.Message}";
            activity.Fail(StatusText);
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath)) File.Delete(temporaryPath);
            CanDownload = true;
            DownloadButtonName = GetDownloadButtonName(SelectedChannel);
        }
    }

    private async Task<HttpResponseMessage> DownloadFromServerAsync(
        SoftwareDownloadChannel channel,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{ClientSession.ApiAddress}/v1/software/download")
        {
            Content = JsonContent.Create(new
            {
                execute = "v1/software/download",
                softwareId = _software.Id,
                channelId = channel.Id
            })
        };
        return await DownloadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static string ResolveFileName(HttpResponseMessage response, string softwareId, SoftwareDownloadChannel channel)
    {
        var value = channel.FileName;
        if (string.IsNullOrWhiteSpace(value))
            value = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName;
        value = value?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(value) && Uri.TryCreate(channel.Url, UriKind.Absolute, out var uri))
            value = Uri.UnescapeDataString(Path.GetFileName(uri.LocalPath));

        value = Path.GetFileName(value);
        if (string.IsNullOrWhiteSpace(value)) value = $"{SanitizeFileName(softwareId)}.download";
        return SanitizeFileName(value);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(value) ? "download.bin" : value;
    }

    private static string GetAvailablePath(string path)
    {
        if (!File.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 1; ; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static async Task VerifySha256Async(string path, string expected, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(actual, expected.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHA-256 校验失败，文件可能已损坏或被替换。");
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }

    private static bool IsWebAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static void OpenAddress(string value)
    {
        if (!IsWebAddress(value)) return;
        Process.Start(new ProcessStartInfo(value) { UseShellExecute = true });
    }

    private static string GetDownloadButtonName(SoftwareDownloadChannel channel) => channel.Mode switch
    {
        SoftwareDownloadMode.Browser => $"前往{channel.Name}",
        SoftwareDownloadMode.Server => $"从{channel.Name}下载",
        _ => $"通过{channel.Name}下载"
    };

    private static HttpClient CreateDownloadClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("XFEToolBox/0.2");
        return client;
    }
}
