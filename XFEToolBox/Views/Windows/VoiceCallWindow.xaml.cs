using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using XFEToolBox.Client.Utilities.Chat;
using XFEToolBox.Core.Model;

namespace XFEToolBox.Client.Views.Windows;

public partial class VoiceCallWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Queue<string> _pendingMessages = new();
    private readonly TaskCompletionSource<bool> _webViewDisposed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _callId;
    private readonly string _selfUserId;
    private readonly string _selfDisplayName;
    private readonly VoiceCallOptions _options;
    private bool _webReady;
    private bool _suppressLeaveNotification;
    private int _leaveNotified;
    private int _webViewDisposeStarted;
    private bool _allowClose;

    public VoiceCallWindow(
        string callId,
        string selfUserId,
        string selfDisplayName,
        string title,
        VoiceCallOptions options)
    {
        _callId = callId;
        _selfUserId = selfUserId;
        _selfDisplayName = selfDisplayName;
        _options = options;
        InitializeComponent();
        WindowTitleText.Text = string.IsNullOrWhiteSpace(title) ? "语音通话" : title;
        Loaded += VoiceCallWindow_Loaded;
    }

    public event EventHandler<VoiceCallSignalEventArgs>? SignalGenerated;

    public event EventHandler? LeaveRequested;

    public Task UpdateParticipantsAsync(IReadOnlyList<VoiceCallParticipant> participants) =>
        PostHostMessageAsync(new
        {
            type = "call.participants",
            callId = _callId,
            participants
        });

    public Task RemoveParticipantAsync(string userId) =>
        PostHostMessageAsync(new
        {
            type = "call.participant-left",
            callId = _callId,
            userId
        });

    public Task ApplyRemoteSignalAsync(string signalType, string fromUserId, JsonElement signal) =>
        PostHostMessageAsync(new
        {
            type = "webrtc.signal",
            callId = _callId,
            signalType,
            fromUserId,
            signal
        });

    public async Task SetRealtimeStateAsync(string state, string? error)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            RealtimeStatusText.Text = state switch
            {
                nameof(ChatRealtimeConnectionState.Connected) => "信令已连接",
                nameof(ChatRealtimeConnectionState.Reconnecting) => "信令重连中",
                nameof(ChatRealtimeConnectionState.WaitingForLogin) => "登录已失效",
                _ => state
            };
        });
        await PostHostMessageAsync(new { type = "realtime.state", state, error });
    }

    public void SuppressLeaveNotification() => _suppressLeaveNotification = true;

    public Task WaitForDisposalAsync() => _webViewDisposed.Task;

    private async void VoiceCallWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(AppPath.AppLocalData, "WebView2", "VoiceCall");
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            if (Volatile.Read(ref _webViewDisposeStarted) != 0) return;
            await CallWebView.EnsureCoreWebView2Async(environment);
            if (Volatile.Read(ref _webViewDisposeStarted) != 0) return;
            ConfigureWebView(CallWebView.CoreWebView2);

            var assets = FindAssetDirectory();
            if (assets is null)
                throw new DirectoryNotFoundException("找不到 Resources/ChatCall 通话页面资源。请确认发布输出已复制该目录。");
            CallWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "call.xfetoolbox",
                assets,
                CoreWebView2HostResourceAccessKind.DenyCors);
            CallWebView.CoreWebView2.Navigate("https://call.xfetoolbox/call.html");
        }
        catch (Exception exception)
        {
            if (Volatile.Read(ref _webViewDisposeStarted) != 0) return;
            LoadingDetailText.Text = exception is WebView2RuntimeNotFoundException
                ? "未检测到 Microsoft Edge WebView2 Runtime。"
                : exception.Message;
            RealtimeStatusText.Text = "初始化失败";
        }
    }

    private void ConfigureWebView(CoreWebView2 webView)
    {
        webView.Settings.AreDefaultContextMenusEnabled = false;
        webView.Settings.AreDevToolsEnabled = false;
        webView.Settings.AreBrowserAcceleratorKeysEnabled = false;
        webView.Settings.IsStatusBarEnabled = false;
        webView.WebMessageReceived += WebView_WebMessageReceived;
        webView.PermissionRequested += WebView_PermissionRequested;
        webView.ProcessFailed += WebView_ProcessFailed;
    }

    private void WebView_ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs args) =>
        Dispatcher.Invoke(() =>
        {
            if (Volatile.Read(ref _webViewDisposeStarted) != 0) return;
            RealtimeStatusText.Text = "通话页面异常";
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingDetailText.Text = $"WebView2 进程异常：{args.ProcessFailedKind}";
        });

    private void WebView_PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        var trustedOrigin = Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) &&
                            string.Equals(uri.Host, "call.xfetoolbox", StringComparison.OrdinalIgnoreCase);
        e.State = trustedOrigin && e.PermissionKind == CoreWebView2PermissionKind.Microphone
            ? CoreWebView2PermissionState.Allow
            : CoreWebView2PermissionState.Deny;
        e.Handled = true;
    }

    private void WebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (Volatile.Read(ref _webViewDisposeStarted) != 0) return;
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (!TryGetString(root, "type", out var type))
                return;
            switch (type)
            {
                case "ready":
                    _ = CompleteWebInitializationAsync();
                    break;
                case "signal":
                    HandleGeneratedSignal(root);
                    break;
                case "leave":
                    NotifyLeaveRequested();
                    break;
                case "status":
                    if (TryGetString(root, "message", out var status))
                        RealtimeStatusText.Text = status;
                    break;
                case "error":
                    if (TryGetString(root, "message", out var error))
                    {
                        RealtimeStatusText.Text = "音频异常";
                        LoadingOverlay.Visibility = Visibility.Visible;
                        LoadingDetailText.Text = error;
                    }
                    break;
            }
        }
        catch (JsonException)
        {
            RealtimeStatusText.Text = "收到无效的通话页面消息";
        }
    }

    private async Task CompleteWebInitializationAsync()
    {
        if (Volatile.Read(ref _webViewDisposeStarted) != 0 || CallWebView.CoreWebView2 is null)
            return;
        _webReady = true;
        CallWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "host.init",
            callId = _callId,
            selfUserId = _selfUserId,
            selfDisplayName = _selfDisplayName,
            maximumParticipants = _options.MaximumMeshParticipants,
            startWithNoiseSuppression = _options.StartWithNoiseSuppression,
            iceServers = _options.IceServers.Select(server => new
            {
                urls = server.Urls,
                username = server.UserName,
                credential = server.Credential
            })
        }, JsonOptions));

        while (_pendingMessages.Count > 0)
            CallWebView.CoreWebView2.PostWebMessageAsJson(_pendingMessages.Dequeue());
        LoadingOverlay.Visibility = Visibility.Collapsed;
        RealtimeStatusText.Text = "正在申请麦克风权限";
        await Task.CompletedTask;
    }

    private void HandleGeneratedSignal(JsonElement root)
    {
        if (!TryGetString(root, "callId", out var callId) ||
            !string.Equals(callId, _callId, StringComparison.Ordinal) ||
            !TryGetString(root, "signalType", out var signalType) ||
            !TryGetString(root, "targetUserId", out var targetUserId) ||
            !root.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object)
            return;
        if (signalType is not ("webrtc.offer" or "webrtc.answer" or "webrtc.ice"))
            return;
        var normalizedPayload = payload.EnumerateObject()
            .ToDictionary(property => property.Name, property => (object?)property.Value.Clone(), StringComparer.Ordinal);
        normalizedPayload["callId"] = callId;
        normalizedPayload["targetUserId"] = targetUserId;
        SignalGenerated?.Invoke(this, new VoiceCallSignalEventArgs(
            callId,
            signalType,
            JsonSerializer.SerializeToElement(normalizedPayload, JsonOptions)));
    }

    private async Task PostHostMessageAsync(object message)
    {
        if (Volatile.Read(ref _webViewDisposeStarted) != 0) return;
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await Dispatcher.InvokeAsync(() =>
        {
            if (Volatile.Read(ref _webViewDisposeStarted) != 0) return;
            if (_webReady && CallWebView.CoreWebView2 is not null)
                CallWebView.CoreWebView2.PostWebMessageAsJson(json);
            else
                _pendingMessages.Enqueue(json);
        });
    }

    private static string? FindAssetDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Resources", "ChatCall"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Resources", "ChatCall")),
            Path.Combine(Environment.CurrentDirectory, "XFEToolBox", "Resources", "ChatCall")
        };
        return candidates.FirstOrDefault(path => File.Exists(Path.Combine(path, "call.html")));
    }

    private void NotifyLeaveRequested()
    {
        if (_suppressLeaveNotification || Interlocked.Exchange(ref _leaveNotified, 1) != 0)
            return;
        LeaveRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        NotifyLeaveRequested();
        if (!_allowClose)
        {
            e.Cancel = true;
            if (Interlocked.CompareExchange(ref _webViewDisposeStarted, 1, 0) == 0)
                _ = DisposeWebViewAndCloseAsync();
            return;
        }
        base.OnClosing(e);
    }

    private async Task DisposeWebViewAndCloseAsync()
    {
        try
        {
            var webView = CallWebView.CoreWebView2;
            if (_webReady && webView is not null)
            {
                try
                {
                    webView.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "host.dispose" }, JsonOptions));
                    await Task.Delay(100);
                }
                catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
                {
                }
            }

            _webReady = false;
            _pendingMessages.Clear();
            Loaded -= VoiceCallWindow_Loaded;
            if (webView is not null)
            {
                webView.WebMessageReceived -= WebView_WebMessageReceived;
                webView.PermissionRequested -= WebView_PermissionRequested;
                webView.ProcessFailed -= WebView_ProcessFailed;
            }
            CallWebView.Dispose();
        }
        catch
        {
        }
        finally
        {
            _allowClose = true;
            try
            {
                if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                    await Dispatcher.InvokeAsync(Close);
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                _webViewDisposed.TrySetResult(true);
            }
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
            return false;
        value = property.GetString()?.Trim() ?? string.Empty;
        return value.Length > 0;
    }
}

public sealed class VoiceCallSignalEventArgs(
    string callId,
    string signalType,
    JsonElement payload) : EventArgs
{
    public string CallId { get; } = callId;

    public string SignalType { get; } = signalType;

    public JsonElement Payload { get; } = payload;
}
