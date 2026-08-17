using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Net;
using System.Windows.Controls;
using System.Windows.Media;
using XFEExtension.NetCore.FormatExtension;
using XFEExtension.NetCore.XFEConsole;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Views.Pages;

namespace XFEToolBox.Client.ViewModel.Pages;

public partial class ConsolePageViewModel : ObservableObject
{
    [ObservableProperty]
    private bool canStartServer = true;
    [ObservableProperty]
    private bool canStopServer = false;
    [ObservableProperty]
    private bool canRestartServer = false;
    [ObservableProperty]
    private bool canCleanUp = false;

    private bool autoScroll = true;
    private readonly BufferedConsoleRenderer outputRenderer;
    public ConsolePage ViewPage { get; set; }
    public XFEConsoleTerminalServer? TerminalServer { get; set; }
    public XFEConsoleTerminalClient? TerminalClient { get; set; }

    public ConsolePageViewModel(ConsolePage viewPage)
    {
        ViewPage = viewPage;
        outputRenderer = new(viewPage.Dispatcher, viewPage.consoleListBox, () => ConsoleProfile.MaxLine);
        outputRenderer.ContentChanged += hasContent =>
        {
            CanCleanUp = hasContent;
            if (hasContent && autoScroll)
                ViewPage.ScrollConsoleToEnd();
        };
    }

    public async Task StartConsoleServer()
    {
        TerminalServer = new XFEConsoleTerminalServer(ConsoleProfile.ConsolePort, ConsoleProfile.LocalHostOnly, ConsoleProfile.ConsolePassword);
        TerminalServer.ServerStarted += TerminalServer_ServerStarted;
        TerminalServer.Connected += TerminalServer_Connected;
        TerminalServer.Disconnected += TerminalServer_Disconnected;
        TerminalServer.MessageReceived += TerminalServer_MessageReceived;
        TerminalServer.ErrorOccurred += TerminalServer_ErrorOccurred;
        await TerminalServer.StartAsync();
    }

    public async Task StartRemoteConsoleClient()
    {
        if (TerminalClient is not null)
            await TerminalClient.DisposeAsync();
        TerminalClient = new XFEConsoleTerminalClient(
            NormalizeRemoteAddress(ConsoleProfile.RemoteServerAddress),
            ConsoleProfile.RemoteServerPassword,
            Environment.MachineName,
            $"XFEToolBox-{Environment.ProcessId}");
        TerminalClient.Connected += TerminalClient_Connected;
        TerminalClient.Disconnected += TerminalClient_Disconnected;
        TerminalClient.MessageReceived += TerminalClient_MessageReceived;
        TerminalClient.ErrorOccurred += TerminalClient_ErrorOccurred;
        if (!await TerminalClient.ConnectAsync())
            throw new UnauthorizedAccessException(TerminalClient.AuthenticationFailureReason ?? "远程调试服务器拒绝了连接密码。");
    }

    public Task StartConsoleEndpoint() => ConsoleProfile.ConnectToRemoteServer
        ? StartRemoteConsoleClient()
        : StartConsoleServer();

    public async Task ShutDownEndpoint()
    {
        var terminalClient = TerminalClient;
        TerminalClient = null;
        if (terminalClient is not null)
        {
            await terminalClient.DisconnectAsync();
            await terminalClient.DisposeAsync();
        }

        var terminalServer = TerminalServer;
        TerminalServer = null;
        if (terminalServer is not null)
            await terminalServer.StopAsync();
    }

    private static string NormalizeRemoteAddress(string? address)
    {
        var normalized = string.IsNullOrWhiteSpace(address)
            ? $"ws://localhost:{ConsoleProfile.ConsolePort}/"
            : address.Trim();
        if (!normalized.Contains("://", StringComparison.Ordinal))
            normalized = $"ws://{normalized}";
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            var builder = new UriBuilder(uri);
            if (builder.Scheme == Uri.UriSchemeHttp)
                builder.Scheme = "ws";
            else if (builder.Scheme == Uri.UriSchemeHttps)
                builder.Scheme = "wss";
            return builder.Uri.ToString();
        }
        return normalized;
    }

    private void TerminalServer_ErrorOccurred(XFEConsoleClientInfo sender, Exception e)
    {
        var code = e.InnerException?.InnerException is HttpListenerException ? (e.InnerException.InnerException as HttpListenerException)!.ErrorCode : 0;
        if (code != 995)
            ShowMessageWithTimeLine($"[foldblock color: white #ff0000 title: 错误：{e.Message} text: {e}]");
    }

    private void TerminalServer_MessageReceived(XFEConsoleClientInfo sender, string e)
    {
        var dictionary = new XFEDictionary(e);
        var isLine = dictionary["IsLine"] == "true";
        var textMessage = dictionary["Text"] ?? string.Empty;
        outputRenderer.Append(
            startsNewLine => startsNewLine
                ? $"[{DateTime.Now:HH:mm:ss}] {sender.ClientName}> {textMessage}"
                : textMessage,
            Colors.White,
            isLine);
    }

    private void TerminalServer_Disconnected(XFEConsoleTerminalServer sender, XFEConsoleClientInfo e)
    {
        ShowMessageWithTimeLine($"[color #9898e7]客户端断开连接 [color white]{e.ClientName}");
    }

    private void TerminalServer_Connected(XFEConsoleTerminalServer sender, XFEConsoleClientInfo e)
    {
        ShowMessageWithTimeLine($"[color #9898e7]客户端连接 [color white]{e.ClientName}");
    }

    private void TerminalServer_ServerStarted(XFEConsoleTerminalServer sender)
    {
        ShowMessageWithTimeLine($"控制台服务器已在端口：[hyperlink color: #05ff00 link: http://localhost:{ConsoleProfile.ConsolePort}/ text: {ConsoleProfile.ConsolePort}] 上运行");
    }

    private void TerminalClient_Connected(XFEConsoleTerminalClient sender)
    {
        var programName = string.IsNullOrWhiteSpace(sender.RemoteProgramName) ? sender.ServerAddress : sender.RemoteProgramName;
        ShowMessageWithTimeLine($"[color #9898e7]已连接远程调试程序 [color white]{programName}");
    }

    private void TerminalClient_Disconnected(XFEConsoleTerminalClient sender)
    {
        ShowMessageWithTimeLine("[color #9898e7]远程调试程序已断开连接");
        ViewPage.Dispatcher.BeginInvoke(() =>
        {
            CanStartServer = true;
            CanStopServer = false;
            CanRestartServer = false;
        });
    }

    private void TerminalClient_MessageReceived(XFEConsoleTerminalClient sender, string message)
    {
        var dictionary = new XFEDictionary(message);
        var isLine = dictionary["IsLine"] == "true";
        var textMessage = dictionary["Text"] ?? string.Empty;
        var programName = string.IsNullOrWhiteSpace(sender.RemoteProgramName) ? "远程程序" : sender.RemoteProgramName;
        outputRenderer.Append(
            startsNewLine => startsNewLine
                ? $"[{DateTime.Now:HH:mm:ss}] {programName}> {textMessage}"
                : textMessage,
            Colors.White,
            isLine);
    }

    private void TerminalClient_ErrorOccurred(XFEConsoleTerminalClient sender, Exception exception)
        => ShowMessageWithTimeLine($"[foldblock color: white #ff0000 title: 远程连接错误：{exception.Message} text: {exception}]");

    internal void ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange == 0 && e.VerticalChange < 0)
            autoScroll = false;
        else if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 0.5)
            autoScroll = true;
        if (autoScroll && e.ExtentHeightChange > 0 && sender is ScrollViewer scrollViewer)
            scrollViewer.ScrollToEnd();
    }

    /// <summary>
    /// 显示消息
    /// </summary>
    /// <param name="message"></param>
    /// <param name="defaultColor"></param>
    public void ShowMessage(string message, Color defaultColor, bool isLineEnd, bool forceLine = false)
        => outputRenderer.Append(message, defaultColor, isLineEnd, forceLine);

    /// <summary>
    /// 显示消息
    /// </summary>
    /// <param name="message"></param>
    /// <param name="defaultColor"></param>
    public async Task ShowMessageAsync(string message, Color defaultColor, bool isLineEnd, bool forceLine = false)
        => await outputRenderer.AppendAsync(message, defaultColor, isLineEnd, forceLine);

    /// <summary>
    /// 显示消息
    /// </summary>
    /// <param name="message"></param>
    public void ShowMessage(string message, bool isLineEnd = true, bool forceLine = false) => ShowMessage(message, Colors.White, isLineEnd, forceLine);

    /// <summary>
    /// 显示消息
    /// </summary>
    /// <param name="message"></param>
    public async Task ShowMessageAsync(string message, bool isLineEnd = true, bool forceLine = false) => await ShowMessageAsync(message, Colors.White, isLineEnd, forceLine);

    /// <summary>
    /// 显示附带时间轴的消息
    /// </summary>
    /// <param name="message"></param>
    public void ShowMessageWithTimeLine(string message, bool isLineEnd = true) => ShowMessage($"[{DateTime.Now:HH:mm:ss}] {message}", isLineEnd, true);

    /// <summary>
    /// 显示附带时间轴的消息
    /// </summary>
    /// <param name="message"></param>
    public async Task ShowMessageWithTimeLineAsync(string message, bool isLineEnd = true) => await ShowMessageAsync($"[{DateTime.Now:HH:mm:ss}] {message}", isLineEnd, true);

    #region Command
    [RelayCommand]
    private async Task StartServer()
    {
        CanRestartServer = false;
        CanStartServer = false;
        try
        {
            await ShowMessageWithTimeLineAsync(ConsoleProfile.ConnectToRemoteServer
                ? "[color yellow]正在连接远程调试服务器..."
                : "[color yellow]正在启动控制台服务器...");
            await StartConsoleEndpoint();
            CanStopServer = true;
            CanRestartServer = true;
        }
        catch (Exception ex)
        {
            await ShutDownEndpoint();
            await ShowMessageWithTimeLineAsync($"[color red]无法建立调试连接：{ex.Message}");
            CanRestartServer = false;
            CanStopServer = false;
            CanStartServer = true;
        }
    }
    [RelayCommand]
    private async Task StopServer()
    {
        CanRestartServer = false;
        CanStopServer = false;
        try
        {
            await ShutDownEndpoint();
            CanStartServer = true;
            await ShowMessageWithTimeLineAsync("[color yellow]调试连接已关闭");
        }
        catch (Exception ex)
        {
            await ShowMessageWithTimeLineAsync($"[color red]关闭调试连接时出现错误：{ex}");
            CanStartServer = TerminalServer is null && TerminalClient is null;
            CanRestartServer = !CanStartServer;
            CanStopServer = !CanStartServer;
        }
    }
    [RelayCommand]
    private async Task RestartServer()
    {
        CanRestartServer = false;
        CanStartServer = false;
        CanStopServer = false;
        try
        {
            await ShutDownEndpoint();
            await ShowMessageWithTimeLineAsync("[color yellow]调试连接已关闭");
            await StartConsoleEndpoint();
            CanStartServer = false;
            CanStopServer = true;
            CanRestartServer = true;
            await ShowMessageWithTimeLineAsync(ConsoleProfile.ConnectToRemoteServer
                ? "[color yellow]远程调试服务器重连完成"
                : "[color yellow]控制台服务器重启完成");
        }
        catch (Exception ex)
        {
            await ShowMessageWithTimeLineAsync($"[color red]重启服务器时出现错误：{ex}");
            CanStartServer = TerminalServer is null && TerminalClient is null;
            CanRestartServer = !CanStartServer;
            CanStopServer = !CanStartServer;
        }
    }
    [RelayCommand]
    private void CleanUp()
    {
        outputRenderer.Clear();
    }
    #endregion
}
