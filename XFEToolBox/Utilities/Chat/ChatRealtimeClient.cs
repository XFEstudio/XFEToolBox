using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using XFEToolBox.Client.Utilities.Server;

namespace XFEToolBox.Client.Utilities.Chat;

public sealed class ChatRealtimeClient : IAsyncDisposable
{
    private const int MaximumInboundMessageBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _lifecycleSync = new();
    private readonly ConcurrentDictionary<string, PendingRealtimeRequest> _pendingRequests = new(StringComparer.Ordinal);
    private CancellationTokenSource? _lifetime;
    private ClientWebSocket? _socket;
    private Task? _runTask;
    private long _runGeneration;
    private int _state = (int)ChatRealtimeConnectionState.Stopped;
    private int _disposed;

    public static ChatRealtimeClient Shared { get; } = new();

    public event EventHandler<ChatRealtimeEnvelopeEventArgs>? EnvelopeReceived;

    public event EventHandler<ChatRealtimeConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public ChatRealtimeConnectionState State =>
        (ChatRealtimeConnectionState)Volatile.Read(ref _state);

    public bool IsConnected => State == ChatRealtimeConnectionState.Connected &&
                               _socket?.State == WebSocketState.Open;

    public string? ConnectionId { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Task? previousRun;
            lock (_lifecycleSync)
            {
                if (_runTask is { IsCompleted: false } && _lifetime?.IsCancellationRequested == false)
                    return;
                previousRun = _runTask;
            }

            if (previousRun is not null)
            {
                try { await previousRun.WaitAsync(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            }

            lock (_lifecycleSync)
            {
                _lifetime?.Dispose();
                var lifetime = new CancellationTokenSource();
                _lifetime = lifetime;
                var generation = checked(++_runGeneration);
                _runTask = Task.Run(
                    () => RunAsync(generation, lifetime.Token),
                    CancellationToken.None);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task<bool> WaitUntilConnectedAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (IsConnected)
            return true;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStateChanged(object? sender, ChatRealtimeConnectionStateChangedEventArgs args)
        {
            if (args.State == ChatRealtimeConnectionState.Connected)
                completion.TrySetResult(true);
        }

        ConnectionStateChanged += OnStateChanged;
        try
        {
            if (IsConnected)
                return true;
            await completion.Task.WaitAsync(timeout, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        finally
        {
            ConnectionStateChanged -= OnStateChanged;
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Task? runTask;
            ClientWebSocket? socket;
            CancellationTokenSource? lifetime;
            long generation;
            lock (_lifecycleSync)
            {
                lifetime = _lifetime;
                lifetime?.Cancel();
                runTask = _runTask;
                socket = _socket;
                generation = _runGeneration;
            }
            FailPendingRequests(
                generation,
                new OperationCanceledException("实时通信连接正在停止。"));

            if (socket is not null)
                await TryCloseSocketAsync(socket, WebSocketCloseStatus.NormalClosure, "client stopping")
                    .ConfigureAwait(false);
            if (runTask is not null)
            {
                try { await runTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }

            lock (_lifecycleSync)
            {
                if (generation == _runGeneration)
                {
                    _runTask = null;
                    _socket = null;
                    _lifetime = null;
                }
            }
            lifetime?.Dispose();
            ChangeState(generation, ChatRealtimeConnectionState.Stopped, null);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Requests shutdown without waiting for socket or receive-loop completion. This
    /// is safe to call from WPF's synchronous OnExit path.
    /// </summary>
    public void RequestStop()
    {
        CancellationTokenSource? lifetime;
        ClientWebSocket? socket;
        long generation;
        lock (_lifecycleSync)
        {
            lifetime = _lifetime;
            socket = _socket;
            generation = _runGeneration;
        }
        lifetime?.Cancel();
        try { socket?.Abort(); }
        catch (ObjectDisposedException) { }
        FailPendingRequests(generation, new OperationCanceledException("实时通信连接正在停止。"));
    }

    public Task<bool> SendAsync(
        string type,
        object? payload = null,
        string? conversationId = null,
        CancellationToken cancellationToken = default) =>
        SendEnvelopeAsync(new ChatClientRealtimeEnvelope
        {
            Type = type,
            ConversationId = conversationId,
            Payload = JsonSerializer.SerializeToElement(payload ?? new { }, JsonOptions)
        }, cancellationToken);

    public async Task<ChatClientRealtimeEnvelope> SendRequestAsync(
        string type,
        object? payload = null,
        string? conversationId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var generation = Volatile.Read(ref _runGeneration);
        var envelope = new ChatClientRealtimeEnvelope
        {
            Type = type,
            ConversationId = conversationId,
            Payload = JsonSerializer.SerializeToElement(payload ?? new { }, JsonOptions)
        };
        var pending = new PendingRealtimeRequest(generation);
        if (!_pendingRequests.TryAdd(envelope.EventId, pending))
            throw new InvalidOperationException("无法登记实时请求。请重试。");

        try
        {
            if (!await SendEnvelopeAsync(envelope, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("实时连接尚未就绪，无法发送请求。");
            return await pending.Completion.Task
                .WaitAsync(timeout ?? TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new ChatRealtimeRequestException(
                "realtime.timeout",
                "实时服务响应超时，请重试。",
                envelope.EventId,
                exception);
        }
        finally
        {
            _pendingRequests.TryRemove(new KeyValuePair<string, PendingRealtimeRequest>(envelope.EventId, pending));
        }
    }

    public async Task<bool> SendEnvelopeAsync(
        ChatClientRealtimeEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        var socket = Volatile.Read(ref _socket);
        if (socket?.State != WebSocketState.Open)
            return false;

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > 64 * 1024)
            throw new InvalidOperationException("实时消息超过 64 KiB 限制。");

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (!ReferenceEquals(socket, Volatile.Read(ref _socket)) || socket.State != WebSocketState.Open)
                return false;
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
            return true;
        }
        catch (Exception exception) when (
            exception is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task RunAsync(long generation, CancellationToken cancellationToken)
    {
        var reconnectAttempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            ClientWebSocket? socket = null;
            try
            {
                if (!ClientSession.IsLoggedIn)
                {
                    ChangeState(generation, ChatRealtimeConnectionState.WaitingForLogin, null);
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                ChangeState(generation, reconnectAttempt == 0
                    ? ChatRealtimeConnectionState.Connecting
                    : ChatRealtimeConnectionState.Reconnecting, null);
                var ticket = await RequestTicketAsync(cancellationToken).ConfigureAwait(false);
                socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                socket.Options.SetRequestHeader("X-XFE-Realtime-Client", "XFEToolBox-WPF");
                await socket.ConnectAsync(BuildWebSocketUri(ticket), cancellationToken).ConfigureAwait(false);

                lock (_lifecycleSync)
                {
                    if (generation != _runGeneration || cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(cancellationToken);
                    _socket = socket;
                }
                reconnectAttempt = 0;
                ChangeState(generation, ChatRealtimeConnectionState.Connected, null);

                using var connectionLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var heartbeatTask = RunHeartbeatAsync(socket, connectionLifetime.Token);
                try
                {
                    await ReceiveLoopAsync(socket, generation, connectionLifetime.Token).ConfigureAwait(false);
                }
                finally
                {
                    connectionLifetime.Cancel();
                    try { await heartbeatTask.ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                ChangeState(generation, ChatRealtimeConnectionState.Reconnecting, exception);
            }
            finally
            {
                FailPendingRequests(
                    generation,
                    new ChatRealtimeRequestException(
                        "realtime.disconnected",
                        "实时通信连接已断开。",
                        null));
                ConnectionId = null;
                lock (_lifecycleSync)
                {
                    if (ReferenceEquals(_socket, socket))
                        _socket = null;
                }
                if (socket is not null)
                {
                    await TryCloseSocketAsync(socket, WebSocketCloseStatus.NormalClosure, "reconnecting")
                        .ConfigureAwait(false);
                    socket.Dispose();
                }
            }

            if (cancellationToken.IsCancellationRequested)
                break;
            ChangeState(generation, ChatRealtimeConnectionState.Reconnecting, null);
            reconnectAttempt++;
            var exponent = Math.Min(reconnectAttempt, 5);
            var delaySeconds = Math.Min(30, Math.Pow(2, exponent - 1)) + Random.Shared.NextDouble();
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunHeartbeatAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!ReferenceEquals(socket, Volatile.Read(ref _socket)) || socket.State != WebSocketState.Open)
                return;
            if (!await SendAsync("ping", new { clientTimeUtc = DateTimeOffset.UtcNow }, cancellationToken: cancellationToken)
                    .ConfigureAwait(false))
                return;
        }
    }

    private async Task ReceiveLoopAsync(
        ClientWebSocket socket,
        long generation,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;
                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        await socket.CloseOutputAsync(
                            WebSocketCloseStatus.InvalidMessageType,
                            "text frames required",
                            cancellationToken).ConfigureAwait(false);
                        return;
                    }
                    if (message.Length + result.Count > MaximumInboundMessageBytes)
                    {
                        await socket.CloseOutputAsync(
                            WebSocketCloseStatus.MessageTooBig,
                            "message too large",
                            cancellationToken).ConfigureAwait(false);
                        return;
                    }
                    await message.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken).ConfigureAwait(false);
                } while (!result.EndOfMessage);

                var envelope = JsonSerializer.Deserialize<ChatClientRealtimeEnvelope>(
                    message.GetBuffer().AsSpan(0, checked((int)message.Length)),
                    JsonOptions);
                if (envelope is null || string.IsNullOrWhiteSpace(envelope.Type))
                    continue;
                if (envelope.Type == "realtime.connected" &&
                    envelope.Payload.ValueKind == JsonValueKind.Object &&
                    envelope.Payload.TryGetProperty("connectionId", out var connectionId))
                    ConnectionId = connectionId.GetString();
                var completedRequest = TryCompletePendingRequest(generation, envelope);
                if (!completedRequest)
                    RaiseEnvelopeReceived(envelope);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<ChatRealtimeTicketResponse> RequestTicketAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = await ClientSession.Requester.Request<ChatRealtimeTicketResponse>(
            "chatRealtimeTicket",
            ChatRealtimeTicketResponse.RequiredAudience,
            ChatRealtimeTicketResponse.RequiredChannel).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK || response.Result is null ||
            string.IsNullOrWhiteSpace(response.Result.Ticket))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(response.Message)
                ? "无法获取实时通信票据。"
                : response.Message);
        if (response.Result.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("服务器返回了已过期的实时通信票据。");
        return response.Result;
    }

    private static Uri BuildWebSocketUri(ChatRealtimeTicketResponse ticket)
    {
        var api = new Uri(ClientSession.ApiAddress.TrimEnd('/') + "/", UriKind.Absolute);
        var builder = new UriBuilder(api)
        {
            Scheme = api.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
            Port = api.IsDefaultPort ? -1 : api.Port,
            Path = api.AbsolutePath.TrimEnd('/') + "/chat/realtime",
            Query = $"ticket={Uri.EscapeDataString(ticket.Ticket)}" +
                    $"&audience={Uri.EscapeDataString(ticket.Audience)}" +
                    $"&channel={Uri.EscapeDataString(ticket.Channel)}"
        };
        return builder.Uri;
    }

    private bool TryCompletePendingRequest(long generation, ChatClientRealtimeEnvelope envelope)
    {
        if (envelope.Type is not ("event.ack" or "event.error") ||
            envelope.Payload.ValueKind != JsonValueKind.Object ||
            !envelope.Payload.TryGetProperty("replyTo", out var replyToProperty) ||
            replyToProperty.ValueKind != JsonValueKind.String)
            return false;

        var replyTo = replyToProperty.GetString();
        if (string.IsNullOrWhiteSpace(replyTo) ||
            !_pendingRequests.TryGetValue(replyTo, out var pending) ||
            pending.Generation != generation ||
            !_pendingRequests.TryRemove(new KeyValuePair<string, PendingRealtimeRequest>(replyTo, pending)))
            return false;

        if (envelope.Type == "event.ack")
        {
            pending.Completion.TrySetResult(envelope);
            return true;
        }

        var code = envelope.Payload.TryGetProperty("code", out var codeProperty) &&
                   codeProperty.ValueKind == JsonValueKind.String
            ? codeProperty.GetString() ?? "realtime.error"
            : "realtime.error";
        var message = envelope.Payload.TryGetProperty("message", out var messageProperty) &&
                      messageProperty.ValueKind == JsonValueKind.String
            ? messageProperty.GetString() ?? "实时服务拒绝了请求。"
            : "实时服务拒绝了请求。";
        pending.Completion.TrySetException(new ChatRealtimeRequestException(code, message, replyTo));
        return true;
    }

    private void FailPendingRequests(long generation, Exception exception)
    {
        foreach (var pair in _pendingRequests)
        {
            if (pair.Value.Generation != generation ||
                !_pendingRequests.TryRemove(pair))
                continue;
            pair.Value.Completion.TrySetException(exception);
        }
    }

    private void RaiseEnvelopeReceived(ChatClientRealtimeEnvelope envelope)
    {
        var handlers = EnvelopeReceived;
        if (handlers is null) return;
        var args = new ChatRealtimeEnvelopeEventArgs(envelope);
        foreach (EventHandler<ChatRealtimeEnvelopeEventArgs> handler in handlers.GetInvocationList())
        {
            try { handler(this, args); }
            catch { }
        }
    }

    private void RaiseConnectionStateChanged(ChatRealtimeConnectionState state, Exception? error)
    {
        var handlers = ConnectionStateChanged;
        if (handlers is null) return;
        var args = new ChatRealtimeConnectionStateChangedEventArgs(state, error);
        foreach (EventHandler<ChatRealtimeConnectionStateChangedEventArgs> handler in handlers.GetInvocationList())
        {
            try { handler(this, args); }
            catch { }
        }
    }

    private void ChangeState(long generation, ChatRealtimeConnectionState state, Exception? error)
    {
        lock (_lifecycleSync)
        {
            if (generation != _runGeneration)
                return;
        }
        var previous = (ChatRealtimeConnectionState)Interlocked.Exchange(ref _state, (int)state);
        if (previous == state && error is null)
            return;
        RaiseConnectionStateChanged(state, error);
    }

    private static async Task TryCloseSocketAsync(
        ClientWebSocket socket,
        WebSocketCloseStatus status,
        string description)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await socket.CloseOutputAsync(status, description, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            socket.Abort();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await StopAsync();
        _sendLock.Dispose();
        _lifecycleGate.Dispose();
        _lifetime?.Dispose();
    }

    private sealed class PendingRealtimeRequest(long generation)
    {
        public long Generation { get; } = generation;

        public TaskCompletionSource<ChatClientRealtimeEnvelope> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

public sealed class ChatRealtimeRequestException(
    string code,
    string message,
    string? replyTo,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;

    public string? ReplyTo { get; } = replyTo;
}

public sealed class ChatClientRealtimeEnvelope
{
    public int Version { get; init; } = 1;

    public string EventId { get; init; } = Guid.NewGuid().ToString("N");

    public string Type { get; init; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public string? ConversationId { get; init; }

    public string? ActorUserId { get; init; }

    public JsonElement Payload { get; init; }
}

public sealed class ChatRealtimeTicketResponse
{
    public const string RequiredAudience = "chat.realtime";
    public const string RequiredChannel = "chat";

    public string Ticket { get; init; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; init; }

    public string Audience { get; init; } = RequiredAudience;

    public string Channel { get; init; } = RequiredChannel;
}

public sealed class ChatRealtimeEnvelopeEventArgs(ChatClientRealtimeEnvelope envelope) : EventArgs
{
    public ChatClientRealtimeEnvelope Envelope { get; } = envelope;
}

public sealed class ChatRealtimeConnectionStateChangedEventArgs(
    ChatRealtimeConnectionState state,
    Exception? error) : EventArgs
{
    public ChatRealtimeConnectionState State { get; } = state;

    public Exception? Error { get; } = error;
}

public enum ChatRealtimeConnectionState
{
    Stopped,
    WaitingForLogin,
    Connecting,
    Connected,
    Reconnecting
}
