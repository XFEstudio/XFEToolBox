using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
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
        await TerminalServer.StartServer();
    }

    public void ShutDownServer()
    {
        try
        {
            TerminalServer?.Server.Server.Close();
            TerminalServer?.Server.Server.Abort();
        }
        catch { }
        TerminalServer = null;
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
            await ShowMessageWithTimeLineAsync("[color yellow]正在启动服务器...");
            _ = StartConsoleServer();
            CanStopServer = true;
            CanRestartServer = true;
        }
        catch (Exception ex)
        {
            await ShowMessageWithTimeLineAsync($"[color red]无法启动服务器：{ex}");
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
            ShutDownServer();
            CanStartServer = true;
            await ShowMessageWithTimeLineAsync("[color yellow]服务器已关闭");
        }
        catch (Exception ex)
        {
            await ShowMessageWithTimeLineAsync($"[color red]关闭服务器时出现错误：{ex}");
            if (TerminalServer is not null && TerminalServer.Server.Server.IsListening)
            {
                CanRestartServer = true;
                CanStopServer = true;
            }
            else
            {
                CanStartServer = true;
            }
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
            ShutDownServer();
            await ShowMessageWithTimeLineAsync("[color yellow]服务器已关闭");
            _ = StartConsoleServer();
            CanStartServer = false;
            CanStopServer = true;
            CanRestartServer = true;
            await ShowMessageWithTimeLineAsync("[color yellow]服务器重启完成");
        }
        catch (Exception ex)
        {
            await ShowMessageWithTimeLineAsync($"[color red]重启服务器时出现错误：{ex}");
            if (TerminalServer is not null && TerminalServer.Server.Server.IsListening)
            {
                CanRestartServer = true;
                CanStopServer = true;
                CanStartServer = false;
            }
            else
            {
                CanRestartServer = false;
                CanStopServer = false;
                CanStartServer = true;
            }
        }
    }
    [RelayCommand]
    private void CleanUp()
    {
        outputRenderer.Clear();
    }
    #endregion
}
