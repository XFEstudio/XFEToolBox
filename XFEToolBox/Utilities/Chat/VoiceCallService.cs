using System.Collections.Concurrent;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Client.Views.Windows;

namespace XFEToolBox.Client.Utilities.Chat;

/// <summary>
/// Coordinates call state over the authenticated realtime client. WebRTC is the
/// primary media path; the same authenticated connection carries low-latency PCM
/// frames automatically when a peer-to-peer path cannot be established.
/// </summary>
public sealed class VoiceCallService : IAsyncDisposable
{
    private static readonly TimeSpan CallCommandTimeout = TimeSpan.FromSeconds(10);
    private static readonly Lazy<VoiceCallService> SharedInstance = new(
        static () => new VoiceCallService(ChatRealtimeClient.Shared),
        LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly ChatRealtimeClient _realtimeClient;
    private readonly VoiceCallOptions _options;
    private readonly Dispatcher _dispatcher;
    private readonly ConcurrentDictionary<string, IncomingVoiceCall> _incomingCalls = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<VoiceCallParticipant>> _participantSnapshots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<PendingVoiceCallSignal>> _pendingSignals = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly SemaphoreSlim _relaySendGate = new(1, 1);
    private VoiceCallWindow? _callWindow;
    private ActiveVoiceCall? _activeCall;
    private int _disposed;

    public static VoiceCallService Current => SharedInstance.Value;

    public VoiceCallService(
        ChatRealtimeClient realtimeClient,
        VoiceCallOptions? options = null,
        Dispatcher? dispatcher = null)
    {
        _realtimeClient = realtimeClient ?? throw new ArgumentNullException(nameof(realtimeClient));
        _options = options ?? VoiceCallOptions.FromEnvironment();
        if (_options.MaximumMeshParticipants is < 2 or > 8)
            throw new ArgumentOutOfRangeException(nameof(options), "Mesh calls support between 2 and 8 participants.");
        _dispatcher = dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _realtimeClient.EnvelopeReceived += RealtimeClient_EnvelopeReceived;
        _realtimeClient.ConnectionStateChanged += RealtimeClient_ConnectionStateChanged;
    }

    public event EventHandler<IncomingVoiceCallEventArgs>? IncomingCallReceived;

    public event EventHandler<VoiceCallErrorEventArgs>? Error;

    public ActiveVoiceCall? ActiveCall => _activeCall;

    /// <summary>Starts the shared signalling connection. Safe to call repeatedly from ChatPage.Loaded.</summary>
    public Task StartAsync() => _realtimeClient.StartAsync();

    public Task StartDirectCallAsync(
        string friendUserId,
        string? conversationId = null,
        CancellationToken cancellationToken = default) =>
        StartCallAsync("user", friendUserId, conversationId, cancellationToken);

    public Task StartGroupCallAsync(
        string groupId,
        string? conversationId = null,
        CancellationToken cancellationToken = default) =>
        StartCallAsync("group", groupId, conversationId ?? groupId, cancellationToken);

    public async Task AcceptAsync(string callId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!_incomingCalls.TryGetValue(callId, out var invitation))
            throw new InvalidOperationException("该通话邀请不存在或已经失效。");

        await EnsureRealtimeConnectedAsync(cancellationToken);
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            EnsureNoOtherCall(callId);
            try
            {
                await _realtimeClient.SendRequestAsync(
                    "call.accept",
                    new { callId },
                    invitation.ConversationId,
                    CallCommandTimeout,
                    cancellationToken);
            }
            catch
            {
                await AbandonCallBestEffortAsync(callId, invitation.ConversationId);
                throw;
            }

            _incomingCalls.TryRemove(callId, out _);
            _activeCall = new ActiveVoiceCall(
                callId,
                invitation.TargetType,
                invitation.TargetId,
                invitation.ConversationId,
                IsIncoming: true);
            await OpenCallWindowAsync(_activeCall, invitation.FromDisplayName);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task RejectAsync(string callId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await EnsureRealtimeConnectedAsync(cancellationToken);
        if (!_incomingCalls.TryGetValue(callId, out var invitation))
            throw new InvalidOperationException("该通话邀请不存在或已经失效。");
        await _realtimeClient.SendRequestAsync(
            "call.reject",
            new { callId },
            invitation.ConversationId,
            CallCommandTimeout,
            cancellationToken);
        _incomingCalls.TryRemove(callId, out _);
        _participantSnapshots.TryRemove(callId, out _);
        _pendingSignals.TryRemove(callId, out _);
    }

    public async Task LeaveAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            var active = _activeCall;
            _activeCall = null;
            if (active is not null)
            {
                _participantSnapshots.TryRemove(active.CallId, out _);
                _pendingSignals.TryRemove(active.CallId, out _);
            }
            try
            {
                if (active is not null && _realtimeClient.IsConnected)
                    await _realtimeClient.SendRequestAsync(
                        "call.leave",
                        new { callId = active.CallId },
                        active.ConversationId,
                        TimeSpan.FromSeconds(4),
                        cancellationToken);
            }
            catch (ChatRealtimeRequestException exception) when (
                exception.Code is "call.not_found" or "call.not_allowed" or "realtime.disconnected")
            {
                // The local call still has to close when the server has already removed it.
            }
            finally
            {
                await CloseCallWindowAsync(suppressLeaveEvent: true);
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task StartCallAsync(
        string targetType,
        string targetId,
        string? conversationId,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        await EnsureRealtimeConnectedAsync(cancellationToken);
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            EnsureNoOtherCall(null);
            var callId = Guid.NewGuid().ToString("N");
            try
            {
                await _realtimeClient.SendRequestAsync(
                    "call.invite",
                    new
                    {
                        callId,
                        targetType,
                        targetId,
                        media = "audio"
                    },
                    conversationId,
                    CallCommandTimeout,
                    cancellationToken);
            }
            catch
            {
                await AbandonCallBestEffortAsync(callId, conversationId);
                throw;
            }

            _activeCall = new ActiveVoiceCall(callId, targetType, targetId, conversationId, IsIncoming: false);
            await OpenCallWindowAsync(_activeCall, targetType == "group" ? "群聊语音" : "好友语音");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private void EnsureNoOtherCall(string? acceptingCallId)
    {
        if (_activeCall is not null &&
            !string.Equals(_activeCall.CallId, acceptingCallId, StringComparison.Ordinal))
            throw new InvalidOperationException("当前已有进行中的通话。");
    }

    private async Task AbandonCallBestEffortAsync(string callId, string? conversationId)
    {
        _participantSnapshots.TryRemove(callId, out _);
        _pendingSignals.TryRemove(callId, out _);
        if (!_realtimeClient.IsConnected) return;
        try
        {
            await _realtimeClient.SendRequestAsync(
                "call.leave",
                new { callId },
                conversationId,
                TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
    }

    private async Task EnsureRealtimeConnectedAsync(CancellationToken cancellationToken)
    {
        await _realtimeClient.StartAsync();
        if (!await _realtimeClient.WaitUntilConnectedAsync(TimeSpan.FromSeconds(15), cancellationToken))
            throw new InvalidOperationException("实时通信服务连接超时，请检查网络后重试。");
    }

    private void RealtimeClient_EnvelopeReceived(object? sender, ChatRealtimeEnvelopeEventArgs e)
    {
        if (e.Envelope.Type == "call.audio")
        {
            ForwardRelayAudioToWindow(e.Envelope);
            return;
        }
        _ = HandleRealtimeEnvelopeSafeAsync(e.Envelope);
    }

    private async Task HandleRealtimeEnvelopeSafeAsync(ChatClientRealtimeEnvelope envelope)
    {
        try
        {
            await HandleRealtimeEnvelopeAsync(envelope);
        }
        catch (Exception exception)
        {
            RaiseError("处理通话事件失败。", exception);
        }
    }

    private async Task HandleRealtimeEnvelopeAsync(ChatClientRealtimeEnvelope envelope)
    {
        switch (envelope.Type)
        {
            case "call.invite":
                HandleIncomingInvite(envelope);
                break;
            case "call.created":
            case "call.accept":
            case "call.join":
                await HandleParticipantsChangedAsync(envelope);
                break;
            case "call.leave":
                await HandleParticipantLeftAsync(envelope);
                break;
            case "call.reject":
                await HandleCallRejectedAsync(envelope);
                break;
            case "call.ended":
                await HandleCallEndedAsync(envelope);
                break;
            case "webrtc.offer":
            case "webrtc.answer":
            case "webrtc.ice":
                await ForwardWebRtcSignalToWindowAsync(envelope);
                break;
            case "event.error":
                HandleServerError(envelope.Payload);
                break;
        }
    }

    private void HandleIncomingInvite(ChatClientRealtimeEnvelope envelope)
    {
        if (!TryGetString(envelope.Payload, "callId", out var callId) ||
            !TryGetString(envelope.Payload, "targetType", out var targetType) ||
            !TryGetString(envelope.Payload, "targetId", out var targetId) ||
            !TryGetString(envelope.Payload, "fromUserId", out var fromUserId))
            return;

        var displayName = GetString(envelope.Payload, "fromDisplayName") ?? fromUserId;
        var invitation = new IncomingVoiceCall(
            callId,
            fromUserId,
            displayName,
            targetType,
            targetId,
            envelope.ConversationId,
            envelope.OccurredAtUtc);
        _incomingCalls[callId] = invitation;
        _dispatcher.BeginInvoke(() =>
            IncomingCallReceived?.Invoke(this, new IncomingVoiceCallEventArgs(invitation)));
    }

    private async Task HandleParticipantsChangedAsync(ChatClientRealtimeEnvelope envelope)
    {
        if (!TryGetString(envelope.Payload, "callId", out var callId)) return;
        var participants = ReadParticipants(envelope.Payload);
        _participantSnapshots[callId] = participants;
        if (!IsActiveCall(callId)) return;
        var window = _callWindow;
        if (window is not null)
            await window.UpdateParticipantsAsync(participants);
    }

    private async Task HandleParticipantLeftAsync(ChatClientRealtimeEnvelope envelope)
    {
        if (!TryGetString(envelope.Payload, "callId", out var callId)) return;
        var participants = ReadParticipants(envelope.Payload);
        _participantSnapshots[callId] = participants;
        if (!IsActiveCall(callId)) return;
        if (TryGetString(envelope.Payload, "userId", out var userId))
        {
            if (_activeCall?.TargetType == "user" &&
                !string.Equals(userId, ClientSession.CurrentUser?.Id, StringComparison.Ordinal))
            {
                await LeaveAsync();
                RaiseError("对方已离开语音通话。", null);
                return;
            }
            if (_callWindow is { } window)
                await window.RemoveParticipantAsync(userId);
        }
        if (_callWindow is { } activeWindow)
            await activeWindow.UpdateParticipantsAsync(participants);
    }

    private async Task HandleCallRejectedAsync(ChatClientRealtimeEnvelope envelope)
    {
        if (!TryGetString(envelope.Payload, "callId", out var callId) || !IsActiveCall(callId)) return;
        if (_activeCall?.TargetType != "user") return;
        var displayName = GetString(envelope.Payload, "displayName") ?? "对方";
        await LeaveAsync();
        RaiseError($"{displayName} 拒绝了语音通话。", null);
    }

    private async Task HandleCallEndedAsync(ChatClientRealtimeEnvelope envelope)
    {
        if (!TryGetString(envelope.Payload, "callId", out var callId) || !IsActiveCall(callId))
            return;
        _activeCall = null;
        _participantSnapshots.TryRemove(callId, out _);
        _pendingSignals.TryRemove(callId, out _);
        await CloseCallWindowAsync(suppressLeaveEvent: true);
    }

    private async Task ForwardWebRtcSignalToWindowAsync(ChatClientRealtimeEnvelope envelope)
    {
        if (!TryGetString(envelope.Payload, "callId", out var callId) ||
            !TryGetString(envelope.Payload, "fromUserId", out var fromUserId) ||
            !envelope.Payload.TryGetProperty("signal", out var signal))
            return;
        if (IsActiveCall(callId) && _callWindow is { } window)
        {
            await window.ApplyRemoteSignalAsync(envelope.Type, fromUserId, signal.Clone());
            return;
        }
        if (!IsActiveCall(callId) &&
            !_incomingCalls.ContainsKey(callId) &&
            !_participantSnapshots.ContainsKey(callId))
            return;
        var queue = _pendingSignals.GetOrAdd(
            callId,
            static _ => new ConcurrentQueue<PendingVoiceCallSignal>());
        while (queue.Count >= 128) queue.TryDequeue(out _);
        queue.Enqueue(new PendingVoiceCallSignal(envelope.Type, fromUserId, signal.Clone()));
    }

    private void ForwardRelayAudioToWindow(ChatClientRealtimeEnvelope envelope)
    {
        if (!TryGetString(envelope.Payload, "callId", out var callId) ||
            !TryGetString(envelope.Payload, "fromUserId", out var fromUserId) ||
            !envelope.Payload.TryGetProperty("sequence", out var sequenceProperty) ||
            !sequenceProperty.TryGetInt64(out var sequence) ||
            sequence < 0 ||
            !envelope.Payload.TryGetProperty("sampleRate", out var sampleRateProperty) ||
            !sampleRateProperty.TryGetInt32(out var sampleRate) ||
            sampleRate != 16_000 ||
            !TryGetString(envelope.Payload, "pcmBase64", out var pcmBase64) ||
            pcmBase64.Length > 1024 ||
            !IsActiveCall(callId) ||
            _callWindow is not { } window)
            return;
        _ = window.ApplyRelayAudioAsync(fromUserId, sequence, sampleRate, pcmBase64);
    }

    private void HandleServerError(JsonElement payload)
    {
        var code = GetString(payload, "code") ?? "realtime.error";
        if (!code.StartsWith("call.", StringComparison.Ordinal) &&
            !code.StartsWith("webrtc.", StringComparison.Ordinal))
            return;
        RaiseError(GetString(payload, "message") ?? "通话服务返回错误。", null);
    }

    private void RealtimeClient_ConnectionStateChanged(
        object? sender,
        ChatRealtimeConnectionStateChangedEventArgs e)
    {
        var window = _callWindow;
        if (window is not null)
            _ = window.SetRealtimeStateAsync(e.State.ToString(), e.Error?.Message);
        if (e.State is ChatRealtimeConnectionState.Reconnecting
            or ChatRealtimeConnectionState.Stopped
            or ChatRealtimeConnectionState.WaitingForLogin)
            _ = EndCallAfterRealtimeLossSafeAsync(e.Error);
    }

    private async Task EndCallAfterRealtimeLossSafeAsync(Exception? exception)
    {
        try
        {
            await _operationLock.WaitAsync();
            try
            {
                var active = _activeCall;
                _activeCall = null;
                _incomingCalls.Clear();
                _participantSnapshots.Clear();
                _pendingSignals.Clear();
                if (active is null)
                    return;
                await CloseCallWindowAsync(suppressLeaveEvent: true);
            }
            finally
            {
                _operationLock.Release();
            }
            RaiseError("实时信令连接已中断，本次通话已结束。请在网络恢复后重新发起。", exception);
        }
        catch (Exception endException)
        {
            RaiseError("实时连接中断后无法完整清理通话。", endException);
        }
    }

    private async Task OpenCallWindowAsync(ActiveVoiceCall call, string title)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            if (_callWindow is not null)
            {
                if (_callWindow.WindowState == WindowState.Minimized)
                    _callWindow.WindowState = WindowState.Normal;
                _callWindow.Activate();
                return;
            }

            var currentUser = ClientSession.CurrentUser
                              ?? throw new InvalidOperationException("必须登录后才能开始通话。");
            _callWindow = new VoiceCallWindow(
                call.CallId,
                currentUser.Id,
                string.IsNullOrWhiteSpace(currentUser.NickName) ? currentUser.UserName : currentUser.NickName,
                title,
                _options);
            _callWindow.SignalGenerated += CallWindow_SignalGenerated;
            _callWindow.RelayAudioGenerated += CallWindow_RelayAudioGenerated;
            _callWindow.LeaveRequested += CallWindow_LeaveRequested;
            _callWindow.Closed += CallWindow_Closed;
            _callWindow.Show();
        });
        if (_participantSnapshots.TryGetValue(call.CallId, out var participants) && _callWindow is { } window)
            await window.UpdateParticipantsAsync(participants);
        if (_pendingSignals.TryRemove(call.CallId, out var signals))
        {
            while (signals.TryDequeue(out var signal) && _callWindow is { } activeWindow)
                await activeWindow.ApplyRemoteSignalAsync(signal.SignalType, signal.FromUserId, signal.Signal);
        }
    }

    private void CallWindow_SignalGenerated(object? sender, VoiceCallSignalEventArgs e) =>
        _ = SendWindowSignalSafeAsync(e);

    private async Task SendWindowSignalSafeAsync(VoiceCallSignalEventArgs signal)
    {
        try
        {
            var active = _activeCall;
            if (active is null || !string.Equals(active.CallId, signal.CallId, StringComparison.Ordinal))
                return;
            if (!await _realtimeClient.SendAsync(signal.SignalType, signal.Payload, active.ConversationId))
                RaiseError("实时连接已断开，WebRTC 信令未能发送。", null);
        }
        catch (Exception exception)
        {
            RaiseError("发送 WebRTC 信令失败。", exception);
        }
    }

    private void CallWindow_RelayAudioGenerated(object? sender, VoiceCallRelayAudioEventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0 || !_relaySendGate.Wait(0))
            return;
        _ = SendWindowRelayAudioSafeAsync(e);
    }

    private async Task SendWindowRelayAudioSafeAsync(VoiceCallRelayAudioEventArgs frame)
    {
        try
        {
            var active = _activeCall;
            if (active is null || !string.Equals(active.CallId, frame.CallId, StringComparison.Ordinal))
                return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _realtimeClient.SendAsync(
                "call.audio",
                new
                {
                    callId = frame.CallId,
                    sequence = frame.Sequence,
                    sampleRate = frame.SampleRate,
                    pcmBase64 = frame.PcmBase64
                },
                active.ConversationId,
                timeout.Token);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            // A late or congested 20 ms frame is intentionally dropped.
        }
        finally
        {
            _relaySendGate.Release();
        }
    }

    private void CallWindow_LeaveRequested(object? sender, EventArgs e) => _ = LeaveSafeAsync();

    private async Task LeaveSafeAsync()
    {
        try { await LeaveAsync(); }
        catch (Exception exception) { RaiseError("离开通话失败。", exception); }
    }

    private void CallWindow_Closed(object? sender, EventArgs e)
    {
        if (sender is VoiceCallWindow window)
        {
            window.SignalGenerated -= CallWindow_SignalGenerated;
            window.RelayAudioGenerated -= CallWindow_RelayAudioGenerated;
            window.LeaveRequested -= CallWindow_LeaveRequested;
            window.Closed -= CallWindow_Closed;
        }
        _callWindow = null;
    }

    private async Task CloseCallWindowAsync(bool suppressLeaveEvent)
    {
        VoiceCallWindow? closingWindow = null;
        await _dispatcher.InvokeAsync(() =>
        {
            var window = _callWindow;
            _callWindow = null;
            if (window is null)
                return;
            closingWindow = window;
            if (suppressLeaveEvent)
                window.SuppressLeaveNotification();
            window.Close();
        });
        if (closingWindow is not null)
            await closingWindow.WaitForDisposalAsync();
    }

    private bool IsActiveCall(string callId) =>
        string.Equals(_activeCall?.CallId, callId, StringComparison.Ordinal);

    private static IReadOnlyList<VoiceCallParticipant> ReadParticipants(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("participants", out var items) ||
            items.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<VoiceCallParticipant>();
        foreach (var item in items.EnumerateArray())
        {
            if (!TryGetString(item, "userId", out var userId))
                continue;
            result.Add(new VoiceCallParticipant(userId, GetString(item, "displayName") ?? userId));
        }
        return result;
    }

    private static bool TryGetString(JsonElement payload, string propertyName, out string value)
    {
        value = string.Empty;
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
            return false;
        value = property.GetString()?.Trim() ?? string.Empty;
        return value.Length > 0;
    }

    private static string? GetString(JsonElement payload, string propertyName) =>
        TryGetString(payload, propertyName, out var value) ? value : null;

    private sealed record PendingVoiceCallSignal(
        string SignalType,
        string FromUserId,
        JsonElement Signal);

    private void RaiseError(string message, Exception? exception) =>
        _dispatcher.BeginInvoke(() => Error?.Invoke(this, new VoiceCallErrorEventArgs(message, exception)));

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _realtimeClient.EnvelopeReceived -= RealtimeClient_EnvelopeReceived;
        _realtimeClient.ConnectionStateChanged -= RealtimeClient_ConnectionStateChanged;
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await LeaveAsync(shutdown.Token); }
        catch { await CloseCallWindowAsync(suppressLeaveEvent: true); }
        _operationLock.Dispose();
    }
}

public sealed class VoiceCallOptions
{
    public IReadOnlyList<VoiceIceServer> IceServers { get; init; } =
        [new VoiceIceServer("stun:stun.l.google.com:19302")];

    public int MaximumMeshParticipants { get; init; } = 8;

    public bool StartWithNoiseSuppression { get; init; } = true;

    /// <summary>
    /// Reads optional deployment ICE settings without embedding long-lived TURN
    /// credentials in source. Separate URLs with semicolons.
    /// </summary>
    public static VoiceCallOptions FromEnvironment()
    {
        var configuredUrls = Environment.GetEnvironmentVariable("XFETOOLBOX_WEBRTC_ICE_SERVERS");
        if (string.IsNullOrWhiteSpace(configuredUrls))
            return new VoiceCallOptions();

        var urls = configuredUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (urls.Length == 0)
            return new VoiceCallOptions();
        var userName = Environment.GetEnvironmentVariable("XFETOOLBOX_TURN_USERNAME");
        var credential = Environment.GetEnvironmentVariable("XFETOOLBOX_TURN_CREDENTIAL");
        return new VoiceCallOptions
        {
            IceServers = [new VoiceIceServer(urls, userName, credential)]
        };
    }
}

public sealed record VoiceIceServer(IReadOnlyList<string> Urls, string? UserName = null, string? Credential = null)
{
    public VoiceIceServer(string url, string? userName = null, string? credential = null)
        : this([url], userName, credential)
    {
    }
}

public sealed record ActiveVoiceCall(
    string CallId,
    string TargetType,
    string TargetId,
    string? ConversationId,
    bool IsIncoming);

public sealed record IncomingVoiceCall(
    string CallId,
    string FromUserId,
    string FromDisplayName,
    string TargetType,
    string TargetId,
    string? ConversationId,
    DateTimeOffset InvitedAtUtc);

public sealed record VoiceCallParticipant(string UserId, string DisplayName);

public sealed class IncomingVoiceCallEventArgs(IncomingVoiceCall invitation) : EventArgs
{
    public IncomingVoiceCall Invitation { get; } = invitation;
}

public sealed class VoiceCallErrorEventArgs(string message, Exception? exception) : EventArgs
{
    public string Message { get; } = message;

    public Exception? Exception { get; } = exception;
}
