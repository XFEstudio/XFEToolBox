using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.Net;
using System.Windows.Controls;
using System.Windows.Media;
using XFEExtension.NetCore.FormatExtension;
using XFEExtension.NetCore.XFEConsole;
using XFEToolBox.Profiles.CrossVersionProfiles;
using XFEToolBox.Utilities;
using XFEToolBox.Views.Pages;

namespace XFEToolBox.ViewModel;

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

    private bool lastLineIsEnd = true;
    public ConsolePage ViewPage { get; set; }
    public XFEConsoleTerminalServer? TerminalServer { get; set; }

    public ConsolePageViewModel(ConsolePage viewPage)
    {
        ViewPage = viewPage;
    }

    public async Task StartConsoleServer()
    {
        TerminalServer = new XFEConsoleTerminalServer(ConsoleProfile.ConsolePort, ConsoleProfile.LocalOnly, ConsoleProfile.ConsolePassword);
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
        }
        catch { }
        TerminalServer = null;
    }

    private void TerminalServer_ErrorOccurred(XFEConsoleClientInfo sender, Exception e)
    {
        var code = e.InnerException?.InnerException is HttpListenerException ? (e.InnerException.InnerException as HttpListenerException)!.ErrorCode : 0;
        if (code != 995)
            ShowMessageWithTimeLine($"[color Red]发生错误：{e.Message}");
    }

    private void TerminalServer_MessageReceived(XFEConsoleClientInfo sender, string e)
    {
        var dictionary = new XFEDictionary(e);
        var isLine = dictionary["IsLine"] == "true";
        var textMessage = dictionary["Text"];
        if (lastLineIsEnd)
            ShowMessageWithTimeLine($"{sender.ClientName}> {textMessage}", isLine);
        else
            ShowMessage($"{textMessage}", isLine);
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
    /// <summary>
    /// 显示消息
    /// </summary>
    /// <param name="message"></param>
    /// <param name="defaultColor"></param>
    public void ShowMessage(string message, Color defaultColor, bool isLineEnd, bool forceLine = false)
    {
        if (!CanCleanUp)
            CanCleanUp = true;
        if (!lastLineIsEnd && !forceLine)
        {
            var lastControl = ViewPage.consoleStackPanel.Children.Count > 0 ? ViewPage.consoleStackPanel.Children[^1] : null;
            if (lastControl is TextBlock lastTextBlock)
            {
                var inLineList = DecoratedTextConverter.ConvertToInlineList(message, defaultColor);
                lastTextBlock.Inlines.AddRange(inLineList);
                lastLineIsEnd = isLineEnd;
                return;
            }
        }
        var textBlock = DecoratedTextConverter.ConvertToTextBlock(message, defaultColor);
        textBlock.TextWrapping = System.Windows.TextWrapping.Wrap;
        textBlock.FontSize = 13;
        textBlock.FontFamily = new FontFamily("Consolas");
        ViewPage.consoleStackPanel.Children.Add(textBlock);
        lastLineIsEnd = isLineEnd;
    }
    /// <summary>
    /// 显示消息
    /// </summary>
    /// <param name="message"></param>
    public void ShowMessage(string message, bool isLineEnd = true, bool forceLine = false) => ShowMessage(message, Colors.White, isLineEnd, forceLine);
    /// <summary>
    /// 显示附带时间轴的消息
    /// </summary>
    /// <param name="message"></param>
    public void ShowMessageWithTimeLine(string message, bool isLineEnd = true) => ShowMessage($"[{DateTime.Now:HH:mm:ss}] {message}", isLineEnd, true);
    [RelayCommand]
    private void StartServer()
    {
        CanRestartServer = false;
        CanStartServer = false;
        try
        {
            ShowMessageWithTimeLine("[color yellow]正在启动服务器...");
            _ = StartConsoleServer();
            CanStopServer = true;
            CanRestartServer = true;
        }
        catch (Exception ex)
        {
            ShowMessageWithTimeLine($"[color red]无法启动服务器：{ex}");
            CanRestartServer = false;
            CanStopServer = false;
            CanStartServer = true;
        }
    }
    [RelayCommand]
    private void StopServer()
    {
        CanRestartServer = false;
        CanStopServer = false;
        try
        {
            ShutDownServer();
            CanStartServer = true;
            ShowMessageWithTimeLine("[color yellow]服务器已关闭");
        }
        catch (Exception ex)
        {
            ShowMessageWithTimeLine($"[color red]关闭服务器时出现错误：{ex}");
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
    private void RestartServer()
    {
        CanRestartServer = false;
        CanStartServer = false;
        CanStopServer = false;
        try
        {
            ShutDownServer();
            ShowMessageWithTimeLine("[color yellow]服务器已关闭");
            _ = StartConsoleServer();
            CanStartServer = false;
            CanStopServer = true;
            CanRestartServer = true;
            ShowMessageWithTimeLine("[color yellow]服务器重启完成");
        }
        catch (Exception ex)
        {
            ShowMessageWithTimeLine($"[color red]重启服务器时出现错误：{ex}");
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
        CanCleanUp = false;
        ViewPage.consoleStackPanel.Children.Clear();
    }
}
