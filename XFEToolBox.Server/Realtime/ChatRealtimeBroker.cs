using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using XFEToolBox.Server.Core.Chat;

namespace XFEToolBox.Server.Realtime;

/// <summary>
/// Authenticated WebSocket broker for durable chat event notifications and call
/// signalling. Audio never traverses this broker; WebRTC carries media directly.
/// </summary>
public sealed class ChatRealtimeBroker : IAsyncDisposable
{
    public const int MaximumConnections = 10_000;
    public const int MaximumConnectionsPerUser = 5;
    public const int MaximumCallParticipants = 8;
    public const int MaximumCallsPerCreator = 3;
    public const int MaximumCallInviteRecipients = 64;
    public const int MaximumTextFrameBytes = 64 * 1024;
    public const int MaximumFramesPerTenSeconds = 160;
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan StaleConnectionTimeout = TimeSpan.FromSeconds(75);

    private readonly ConcurrentDictionary<WebSocket, ChatRealtimeConnection> _connections =
        new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<string, ChatRealtimeConnection> _connectionsById =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ChatRealtimeConnection>> _userConnections =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ChatCallRoom> _callRooms = new(StringComparer.Ordinal);
    private readonly Timer _maintenanceTimer;
    private int _maintenanceRunning;
    private int _disposed;

    public ChatRealtimeBroker(
        ChatRepository chatRepository,
        ChatRealtimeTicketStore ticketStore,
        TimeProvider? timeProvider = null)
    {
        ChatRepository = chatRepository ?? throw new ArgumentNullException(nameof(chatRepository));
        TicketStore = ticketStore ?? throw new ArgumentNullException(nameof(ticketStore));
        TimeProvider = timeProvider ?? TimeProvider.System;
        _maintenanceTimer = new Timer(
            static state => ((ChatRealtimeBroker)state!).StartMaintenance(),
            this,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(15));
    }

    public ChatRepository ChatRepository { get; }

    public ChatRealtimeTicketStore TicketStore { get; }

    public TimeProvider TimeProvider { get; }

    /// <summary>Validates that the HTTP session exchanged for this connection is still active.</summary>
    public Func<ChatRealtimeTicketClaims, bool>? SessionIsActive { private get; set; }

    public int ConnectionCount => _connections.Count;

    public bool TryRegister(WebSocket socket, Uri requestUrl, out string error)
    {
        error = string.Empty;
        if (Volatile.Read(ref _disposed) != 0)
        {
            error = "实时通信服务正在停止。";
            return false;
        }
        if (_connections.Count >= MaximumConnections)
        {
            error = "实时通信连接数已达到上限。";
            return false;
        }

        var query = ParseQuery(requestUrl.Query);
        if (!query.TryGetValue("ticket", out var ticket) ||
            !query.TryGetValue("audience", out var audience) ||
            !query.TryGetValue("channel", out var channel) ||
            !string.Equals(audience, ChatRealtimeTicketStore.RealtimeAudience, StringComparison.Ordinal) ||
            !string.Equals(channel, ChatRealtimeTicketStore.RealtimeChannel, StringComparison.Ordinal) ||
            !TicketStore.TryConsume(ticket, audience, channel, out var claims))
        {
            error = "实时通信票据无效、已使用或已过期。";
            return false;
        }
        if (!IsSessionActive(claims))
        {
            error = "签发实时票据的登录会话已经失效。";
            return false;
        }

        var connection = new ChatRealtimeConnection(socket, claims, TimeProvider);
        if (!_connections.TryAdd(socket, connection) || !_connectionsById.TryAdd(connection.ConnectionId, connection))
        {
            _connections.TryRemove(socket, out _);
            error = "无法注册实时连接。";
            return false;
        }

        var userConnections = _userConnections.GetOrAdd(
            claims.UserId,
            static _ => new ConcurrentDictionary<string, ChatRealtimeConnection>(StringComparer.Ordinal));
        lock (userConnections)
        {
            if (userConnections.Count >= MaximumConnectionsPerUser)
            {
                _connections.TryRemove(socket, out _);
                _connectionsById.TryRemove(connection.ConnectionId, out _);
                error = "每个用户最多允许 5 个实时连接。";
                return false;
            }
            userConnections[connection.ConnectionId] = connection;
        }
        _ = ObserveAsync(connection.RunInboundLoopAsync(HandleInboundAsync), connection);
        _ = SendEnvelopeAsync(connection, ChatRealtimeProtocol.Create("realtime.connected", new
        {
            connectionId = connection.ConnectionId,
            userId = claims.UserId,
            serverTimeUtc = TimeProvider.GetUtcNow(),
            heartbeatIntervalSeconds = (int)HeartbeatInterval.TotalSeconds,
            maximumCallParticipants = MaximumCallParticipants,
            mediaTransport = "webrtc"
        }));
        return true;
    }

    public void ReceiveFrame(WebSocket socket, string message, bool endOfMessage)
    {
        if (!_connections.TryGetValue(socket, out var connection))
        {
            TryAbort(socket);
            return;
        }

        connection.Touch();
        if (!endOfMessage || Encoding.UTF8.GetByteCount(message) > MaximumTextFrameBytes)
        {
            _ = CloseAndUnregisterAsync(connection, WebSocketCloseStatus.MessageTooBig, "frame too large or fragmented");
            return;
        }

        if (!connection.TryConsumeFrameQuota(MaximumFramesPerTenSeconds))
        {
            _ = CloseAndUnregisterAsync(connection, WebSocketCloseStatus.PolicyViolation, "frame rate exceeded");
            return;
        }

        if (!connection.TryQueue(message))
            _ = CloseAndUnregisterAsync(connection, WebSocketCloseStatus.PolicyViolation, "inbound queue full");
    }

    public void RejectBinaryFrame(WebSocket socket)
    {
        if (_connections.TryGetValue(socket, out var connection))
            _ = CloseAndUnregisterAsync(connection, WebSocketCloseStatus.InvalidMessageType, "binary frames are not accepted");
        else
            TryAbort(socket);
    }

    public void ConnectionClosed(WebSocket socket)
    {
        if (_connections.TryGetValue(socket, out var connection))
            _ = UnregisterAsync(connection, closeSocket: false);
    }

    /// <summary>Pushes an already-authorized, already-persisted domain event to every device of one user.</summary>
    public Task PublishToUserAsync(
        string userId,
        string type,
        object payload,
        string? conversationId = null,
        string? actorUserId = null,
        CancellationToken cancellationToken = default) =>
        SendToUsersAsync(
            [userId],
            ChatRealtimeProtocol.Create(type, payload, actorUserId, conversationId),
            cancellationToken);

    /// <summary>Pushes an already-authorized, already-persisted domain event to all current group members.</summary>
    public async Task PublishToGroupAsync(
        string groupId,
        string type,
        object payload,
        string? conversationId = null,
        string? actorUserId = null,
        CancellationToken cancellationToken = default)
    {
        var members = await ChatRepository.GetGroupMemberUserIdsAsync(groupId, cancellationToken);
        await SendToUsersAsync(
            members,
            ChatRealtimeProtocol.Create(type, payload, actorUserId, conversationId ?? groupId),
            cancellationToken);
    }

    public async Task EndDirectCallsAsync(
        string userA,
        string userB,
        string reason = "permission-revoked",
        CancellationToken cancellationToken = default)
    {
        foreach (var room in _callRooms.Values)
        {
            if (room.TargetType != "user" ||
                !((string.Equals(room.CreatorUserId, userA, StringComparison.Ordinal) &&
                   string.Equals(room.TargetId, userB, StringComparison.Ordinal)) ||
                  (string.Equals(room.CreatorUserId, userB, StringComparison.Ordinal) &&
                   string.Equals(room.TargetId, userA, StringComparison.Ordinal))) ||
                !_callRooms.TryRemove(new KeyValuePair<string, ChatCallRoom>(room.CallId, room)))
                continue;

            await SendToRoomAsync(room, ChatRealtimeProtocol.Create("call.ended", new
            {
                callId = room.CallId,
                reason
            }), cancellationToken);
            lock (room.SyncRoot) room.Participants.Clear();
        }
    }

    public async Task EvictUserFromGroupCallsAsync(
        string groupId,
        string userId,
        string reason = "permission-revoked",
        CancellationToken cancellationToken = default)
    {
        foreach (var room in _callRooms.Values)
        {
            if (room.TargetType != "group" ||
                !string.Equals(room.TargetId, groupId, StringComparison.Ordinal))
                continue;
            ChatCallParticipant? removedParticipant;
            bool empty;
            lock (room.SyncRoot)
            {
                room.Participants.TryGetValue(userId, out removedParticipant);
                if (removedParticipant is not null)
                {
                    room.Participants.Remove(userId);
                    room.Touch();
                }
                empty = room.Participants.Count == 0;
            }
            if (removedParticipant is null) continue;
            var participantLeft = CreateParticipantLeft(room, userId, reason);
            if (empty)
                _callRooms.TryRemove(new KeyValuePair<string, ChatCallRoom>(room.CallId, room));
            else
                await SendToRoomAsync(room, participantLeft, cancellationToken);
            if (_connectionsById.TryGetValue(removedParticipant.ConnectionId, out var removedConnection))
                await SendEnvelopeAsync(removedConnection, ChatRealtimeProtocol.Create("call.ended", new
                {
                    callId = room.CallId,
                    reason
                }, userId), cancellationToken);
        }
    }

    private async Task HandleInboundAsync(
        ChatRealtimeConnection connection,
        string message,
        CancellationToken cancellationToken)
    {
        if (!IsSessionActive(connection.Claims))
        {
            await CloseAndUnregisterAsync(connection, WebSocketCloseStatus.PolicyViolation, "login session revoked");
            return;
        }
        if (!ChatRealtimeProtocol.TryParse(message, out var envelope, out var parseError))
        {
            await SendErrorAsync(connection, "protocol.invalid", parseError, null, cancellationToken);
            return;
        }

        if (!connection.TryRememberEventId(envelope.EventId))
        {
            await SendAckAsync(connection, envelope, duplicate: true, cancellationToken);
            return;
        }

        switch (envelope.Type)
        {
            case "ping":
                await SendEnvelopeAsync(connection, ChatRealtimeProtocol.Create("pong", new
                {
                    replyTo = envelope.EventId,
                    serverTimeUtc = TimeProvider.GetUtcNow()
                }), cancellationToken);
                break;
            case "pong":
            case "event.ack":
                break;
            case "call.invite":
                await HandleCallInviteAsync(connection, envelope, cancellationToken);
                break;
            case "call.accept":
                await HandleCallJoinAsync(connection, envelope, "call.accept", cancellationToken);
                break;
            case "call.join":
                await HandleCallJoinAsync(connection, envelope, "call.join", cancellationToken);
                break;
            case "call.reject":
                await HandleCallRejectAsync(connection, envelope, cancellationToken);
                break;
            case "call.leave":
                await HandleCallLeaveAsync(connection, envelope, cancellationToken);
                break;
            case "webrtc.offer":
            case "webrtc.answer":
            case "webrtc.ice":
                await HandleWebRtcSignalAsync(connection, envelope, cancellationToken);
                break;
            default:
                await SendErrorAsync(
                    connection,
                    "protocol.unsupported_event",
                    "不支持的实时事件类型。",
                    envelope.EventId,
                    cancellationToken);
                break;
        }
    }

    private async Task HandleCallInviteAsync(
        ChatRealtimeConnection connection,
        ChatRealtimeEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (!ChatRealtimeProtocol.TryGetRequiredString(envelope.Payload, "targetType", out var targetType) ||
            !ChatRealtimeProtocol.TryGetRequiredString(envelope.Payload, "targetId", out var targetId))
        {
            await SendErrorAsync(connection, "call.invalid_target", "呼叫目标无效。", envelope.EventId, cancellationToken);
            return;
        }

        targetType = targetType.ToLowerInvariant();
        IReadOnlyList<string> recipients;
        if (targetType == "user")
        {
            if (string.Equals(targetId, connection.Claims.UserId, StringComparison.Ordinal) ||
                !await ChatRepository.AreFriendsAsync(connection.Claims.UserId, targetId, cancellationToken))
            {
                await SendErrorAsync(connection, "call.not_allowed", "只能呼叫好友。", envelope.EventId, cancellationToken);
                return;
            }
            recipients = [targetId];
        }
        else if (targetType == "group")
        {
            if (!await ChatRepository.IsGroupMemberAsync(targetId, connection.Claims.UserId, cancellationToken))
            {
                await SendErrorAsync(connection, "call.not_allowed", "你不是该群聊的成员。", envelope.EventId, cancellationToken);
                return;
            }
            recipients = await ChatRepository.GetGroupMemberUserIdsAsync(targetId, cancellationToken);
            if (recipients.Count > MaximumCallInviteRecipients)
            {
                await SendErrorAsync(
                    connection,
                    "call.group_too_large",
                    $"当前 Mesh 通话仅允许向不超过 {MaximumCallInviteRecipients} 人的群聊发起邀请。",
                    envelope.EventId,
                    cancellationToken);
                return;
            }
        }
        else
        {
            await SendErrorAsync(connection, "call.invalid_target", "呼叫目标类型无效。", envelope.EventId, cancellationToken);
            return;
        }

        if (_callRooms.Values.Count(room =>
                string.Equals(room.CreatorUserId, connection.Claims.UserId, StringComparison.Ordinal)) >=
            MaximumCallsPerCreator)
        {
            await SendErrorAsync(
                connection,
                "call.too_many_rooms",
                $"每个用户最多同时发起 {MaximumCallsPerCreator} 个通话。",
                envelope.EventId,
                cancellationToken);
            return;
        }

        var requestedCallId = ChatRealtimeProtocol.GetOptionalString(envelope.Payload, "callId");
        var callId = IsSafeIdentifier(requestedCallId) ? requestedCallId! : Guid.NewGuid().ToString("N");
        var room = new ChatCallRoom(
            callId,
            connection.Claims.UserId,
            targetType,
            targetId,
            TimeProvider);
        room.Participants.Add(connection.Claims.UserId, ToParticipant(connection));
        if (!_callRooms.TryAdd(callId, room))
        {
            await SendErrorAsync(connection, "call.id_conflict", "呼叫标识已存在。", envelope.EventId, cancellationToken);
            return;
        }

        var invitation = ChatRealtimeProtocol.Create("call.invite", new
        {
            callId,
            targetType,
            targetId,
            fromUserId = connection.Claims.UserId,
            fromDisplayName = GetDisplayName(connection),
            maximumParticipants = MaximumCallParticipants,
            media = "audio",
            transport = "webrtc"
        }, connection.Claims.UserId, envelope.ConversationId);

        await SendToUsersAsync(
            recipients.Where(userId => !string.Equals(userId, connection.Claims.UserId, StringComparison.Ordinal)),
            invitation,
            cancellationToken);
        await SendEnvelopeAsync(connection, ChatRealtimeProtocol.Create("call.created", new
        {
            callId,
            targetType,
            targetId,
            participants = SnapshotParticipants(room)
        }, connection.Claims.UserId, envelope.ConversationId), cancellationToken);
        await SendAckAsync(connection, envelope, duplicate: false, cancellationToken, callId);
    }

    private async Task HandleCallJoinAsync(
        ChatRealtimeConnection connection,
        ChatRealtimeEnvelope envelope,
        string eventType,
        CancellationToken cancellationToken)
    {
        if (!TryGetCall(envelope, out var callId, out var room))
        {
            await SendErrorAsync(connection, "call.not_found", "呼叫不存在或已结束。", envelope.EventId, cancellationToken);
            return;
        }
        if (!await IsAllowedInRoomAsync(room, connection.Claims.UserId, cancellationToken))
        {
            await SendErrorAsync(connection, "call.not_allowed", "你无权加入该呼叫。", envelope.EventId, cancellationToken);
            return;
        }

        object participants;
        lock (room.SyncRoot)
        {
            if (room.Participants.TryGetValue(connection.Claims.UserId, out var existing) &&
                !string.Equals(existing.ConnectionId, connection.ConnectionId, StringComparison.Ordinal))
            {
                participants = Array.Empty<object>();
            }
            else if (room.Participants.Count >= MaximumCallParticipants &&
                     !room.Participants.ContainsKey(connection.Claims.UserId))
            {
                participants = null!;
            }
            else
            {
                room.Participants[connection.Claims.UserId] = ToParticipant(connection);
                room.Touch();
                participants = SnapshotParticipantsUnsafe(room);
            }
        }

        if (participants is object[] array && array.Length == 0)
        {
            await SendErrorAsync(connection, "call.joined_on_other_device", "该账号已在另一台设备加入通话。", envelope.EventId, cancellationToken);
            return;
        }
        if (participants is null)
        {
            await SendErrorAsync(connection, "call.full", $"通话最多允许 {MaximumCallParticipants} 人。", envelope.EventId, cancellationToken);
            return;
        }

        await SendToRoomAsync(room, ChatRealtimeProtocol.Create(eventType, new
        {
            callId,
            userId = connection.Claims.UserId,
            displayName = GetDisplayName(connection),
            participants
        }, connection.Claims.UserId, envelope.ConversationId), cancellationToken);
        await SendAckAsync(connection, envelope, duplicate: false, cancellationToken, callId);
    }

    private async Task HandleCallRejectAsync(
        ChatRealtimeConnection connection,
        ChatRealtimeEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (!TryGetCall(envelope, out var callId, out var room) ||
            !await IsAllowedInRoomAsync(room, connection.Claims.UserId, cancellationToken))
        {
            await SendErrorAsync(connection, "call.not_allowed", "呼叫不存在或你无权操作。", envelope.EventId, cancellationToken);
            return;
        }

        var rejection = ChatRealtimeProtocol.Create("call.reject", new
        {
            callId,
            userId = connection.Claims.UserId,
            displayName = GetDisplayName(connection)
        }, connection.Claims.UserId, envelope.ConversationId);
        await SendToUsersAsync([room.CreatorUserId], rejection, cancellationToken);
        await SendAckAsync(connection, envelope, duplicate: false, cancellationToken, callId);
    }

    private async Task HandleCallLeaveAsync(
        ChatRealtimeConnection connection,
        ChatRealtimeEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (!TryGetCall(envelope, out var callId, out var room))
        {
            await SendAckAsync(connection, envelope, duplicate: false, cancellationToken, callId: null);
            return;
        }

        bool removed;
        bool empty;
        lock (room.SyncRoot)
        {
            removed = room.Participants.TryGetValue(connection.Claims.UserId, out var participant) &&
                      string.Equals(participant.ConnectionId, connection.ConnectionId, StringComparison.Ordinal) &&
                      room.Participants.Remove(connection.Claims.UserId);
            room.Touch();
            empty = room.Participants.Count == 0;
        }

        if (empty)
            _callRooms.TryRemove(new KeyValuePair<string, ChatCallRoom>(callId, room));
        if (removed && !empty)
            await SendToRoomAsync(room, CreateParticipantLeft(room, connection.Claims.UserId, "left"), cancellationToken);
        await SendAckAsync(connection, envelope, duplicate: false, cancellationToken, callId);
    }

    private async Task HandleWebRtcSignalAsync(
        ChatRealtimeConnection connection,
        ChatRealtimeEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (!TryGetCall(envelope, out var callId, out var room) ||
            !ChatRealtimeProtocol.TryGetRequiredString(envelope.Payload, "targetUserId", out var targetUserId))
        {
            await SendErrorAsync(connection, "webrtc.invalid_target", "WebRTC 信令目标无效。", envelope.EventId, cancellationToken);
            return;
        }
        if (!await IsAllowedInRoomAsync(room, connection.Claims.UserId, cancellationToken))
        {
            await SendErrorAsync(connection, "webrtc.permission_revoked", "通话权限已经失效。", envelope.EventId, cancellationToken);
            if (room.TargetType == "group")
                await EvictUserFromGroupCallsAsync(room.TargetId, connection.Claims.UserId, "permission-revoked", cancellationToken);
            else
                await EndDirectCallsAsync(room.CreatorUserId, room.TargetId, "permission-revoked", cancellationToken);
            return;
        }

        ChatCallParticipant? sender;
        ChatCallParticipant? target;
        lock (room.SyncRoot)
        {
            room.Participants.TryGetValue(connection.Claims.UserId, out sender);
            room.Participants.TryGetValue(targetUserId, out target);
            room.Touch();
        }
        if (sender is null || target is null ||
            !string.Equals(sender.ConnectionId, connection.ConnectionId, StringComparison.Ordinal) ||
            string.Equals(targetUserId, connection.Claims.UserId, StringComparison.Ordinal))
        {
            await SendErrorAsync(connection, "webrtc.not_allowed", "只能向同一通话中的其他参与者发送信令。", envelope.EventId, cancellationToken);
            return;
        }

        if (!_connectionsById.TryGetValue(target.ConnectionId, out var targetConnection))
        {
            await SendErrorAsync(connection, "webrtc.peer_offline", "目标参与者已离线。", envelope.EventId, cancellationToken);
            return;
        }

        var forwarded = ChatRealtimeProtocol.Create(envelope.Type, new
        {
            callId,
            fromUserId = connection.Claims.UserId,
            fromDisplayName = GetDisplayName(connection),
            targetUserId,
            signal = envelope.Payload.Clone()
        }, connection.Claims.UserId, envelope.ConversationId);
        await SendEnvelopeAsync(targetConnection, forwarded, cancellationToken);
        await SendAckAsync(connection, envelope, duplicate: false, cancellationToken, callId);
    }

    private bool TryGetCall(ChatRealtimeEnvelope envelope, out string callId, out ChatCallRoom room)
    {
        if (ChatRealtimeProtocol.TryGetRequiredString(envelope.Payload, "callId", out callId) &&
            _callRooms.TryGetValue(callId, out var resolvedRoom) && resolvedRoom is not null)
        {
            room = resolvedRoom;
            return true;
        }
        room = default!;
        return false;
    }

    private async Task<bool> IsAllowedInRoomAsync(
        ChatCallRoom room,
        string userId,
        CancellationToken cancellationToken)
    {
        if (room.TargetType == "user")
        {
            if (!string.Equals(userId, room.CreatorUserId, StringComparison.Ordinal) &&
                !string.Equals(userId, room.TargetId, StringComparison.Ordinal))
                return false;
            return string.Equals(userId, room.CreatorUserId, StringComparison.Ordinal) ||
                   await ChatRepository.AreFriendsAsync(room.CreatorUserId, userId, cancellationToken);
        }
        return room.TargetType == "group" &&
               await ChatRepository.IsGroupMemberAsync(room.TargetId, userId, cancellationToken);
    }

    private async Task SendAckAsync(
        ChatRealtimeConnection connection,
        ChatRealtimeEnvelope received,
        bool duplicate,
        CancellationToken cancellationToken,
        string? callId = null) =>
        await SendEnvelopeAsync(connection, ChatRealtimeProtocol.Create("event.ack", new
        {
            replyTo = received.EventId,
            acceptedType = received.Type,
            duplicate,
            callId,
            serverTimeUtc = TimeProvider.GetUtcNow()
        }), cancellationToken);

    private async Task SendErrorAsync(
        ChatRealtimeConnection connection,
        string code,
        string message,
        string? replyTo,
        CancellationToken cancellationToken) =>
        await SendEnvelopeAsync(connection, ChatRealtimeProtocol.Create("event.error", new
        {
            code,
            message,
            replyTo,
            serverTimeUtc = TimeProvider.GetUtcNow()
        }), cancellationToken);

    private async Task SendToRoomAsync(
        ChatCallRoom room,
        ChatRealtimeEnvelope envelope,
        CancellationToken cancellationToken)
    {
        string[] connectionIds;
        lock (room.SyncRoot)
            connectionIds = room.Participants.Values.Select(item => item.ConnectionId).Distinct(StringComparer.Ordinal).ToArray();

        var connections = connectionIds
            .Select(connectionId => _connectionsById.TryGetValue(connectionId, out var connection) ? connection : null)
            .Where(static connection => connection is not null)
            .Cast<ChatRealtimeConnection>()
            .ToArray();
        await SendToConnectionsAsync(connections, envelope, cancellationToken);
    }

    private async Task SendToUsersAsync(
        IEnumerable<string> userIds,
        ChatRealtimeEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var connections = userIds
            .Distinct(StringComparer.Ordinal)
            .SelectMany(userId => _userConnections.TryGetValue(userId, out var items)
                ? items.Values
                : [])
            .DistinctBy(static connection => connection.ConnectionId)
            .ToArray();
        await SendToConnectionsAsync(connections, envelope, cancellationToken);
    }

    private async Task SendToConnectionsAsync(
        IEnumerable<ChatRealtimeConnection> connections,
        ChatRealtimeEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var serialized = JsonSerializer.Serialize(envelope, ChatRealtimeProtocol.JsonOptions);
        await Task.WhenAll(connections.Select(connection => SendSerializedAsync(connection, serialized, cancellationToken)));
    }

    private Task SendEnvelopeAsync(
        ChatRealtimeConnection connection,
        ChatRealtimeEnvelope envelope,
        CancellationToken cancellationToken = default) =>
        SendSerializedAsync(
            connection,
            JsonSerializer.Serialize(envelope, ChatRealtimeProtocol.JsonOptions),
            cancellationToken);

    private async Task SendSerializedAsync(
        ChatRealtimeConnection connection,
        string serialized,
        CancellationToken cancellationToken)
    {
        if (!await connection.SendTextAsync(serialized, cancellationToken))
            await CloseAndUnregisterAsync(connection, WebSocketCloseStatus.EndpointUnavailable, "slow or closed connection");
    }

    private async Task CloseAndUnregisterAsync(
        ChatRealtimeConnection connection,
        WebSocketCloseStatus closeStatus,
        string reason)
    {
        await connection.CloseAsync(closeStatus, reason);
        await UnregisterAsync(connection, closeSocket: false);
    }

    private async Task UnregisterAsync(ChatRealtimeConnection connection, bool closeSocket)
    {
        await connection.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            closeSocket ? "connection closed" : "remote connection closed");
        if (!_connections.TryRemove(new KeyValuePair<WebSocket, ChatRealtimeConnection>(connection.Socket, connection)))
            return;

        _connectionsById.TryRemove(new KeyValuePair<string, ChatRealtimeConnection>(connection.ConnectionId, connection));
        if (_userConnections.TryGetValue(connection.Claims.UserId, out var userConnections))
        {
            userConnections.TryRemove(connection.ConnectionId, out _);
            if (userConnections.IsEmpty)
                _userConnections.TryRemove(new KeyValuePair<string, ConcurrentDictionary<string, ChatRealtimeConnection>>(
                    connection.Claims.UserId,
                    userConnections));
        }

        await RemoveConnectionFromCallsAsync(connection);
    }

    private async Task RemoveConnectionFromCallsAsync(ChatRealtimeConnection connection)
    {
        var changedRooms = new List<ChatCallRoom>();
        foreach (var room in _callRooms.Values)
        {
            bool removed;
            bool empty;
            lock (room.SyncRoot)
            {
                removed = room.Participants.TryGetValue(connection.Claims.UserId, out var participant) &&
                          string.Equals(participant.ConnectionId, connection.ConnectionId, StringComparison.Ordinal) &&
                          room.Participants.Remove(connection.Claims.UserId);
                if (removed)
                    room.Touch();
                empty = room.Participants.Count == 0;
            }
            if (!removed)
                continue;
            if (empty)
                _callRooms.TryRemove(new KeyValuePair<string, ChatCallRoom>(room.CallId, room));
            else
                changedRooms.Add(room);
        }

        foreach (var room in changedRooms)
            await SendToRoomAsync(room, CreateParticipantLeft(room, connection.Claims.UserId, "disconnected"), CancellationToken.None);
    }

    private ChatRealtimeEnvelope CreateParticipantLeft(ChatCallRoom room, string userId, string reason) =>
        ChatRealtimeProtocol.Create("call.leave", new
        {
            callId = room.CallId,
            userId,
            reason,
            participants = SnapshotParticipants(room)
        }, userId);

    private static object[] SnapshotParticipants(ChatCallRoom room)
    {
        lock (room.SyncRoot)
            return SnapshotParticipantsUnsafe(room);
    }

    private static object[] SnapshotParticipantsUnsafe(ChatCallRoom room) => room.Participants.Values
        .Select(static participant => (object)new
        {
            userId = participant.UserId,
            displayName = participant.DisplayName
        })
        .ToArray();

    private static ChatCallParticipant ToParticipant(ChatRealtimeConnection connection) =>
        new(connection.Claims.UserId, connection.ConnectionId, GetDisplayName(connection));

    private static string GetDisplayName(ChatRealtimeConnection connection) =>
        string.IsNullOrWhiteSpace(connection.Claims.NickName)
            ? connection.Claims.UserName
            : connection.Claims.NickName;

    private void StartMaintenance()
    {
        if (Interlocked.CompareExchange(ref _maintenanceRunning, 1, 0) != 0)
            return;
        _ = Task.Run(async () =>
        {
            try
            {
                await RunMaintenanceAsync();
            }
            finally
            {
                Volatile.Write(ref _maintenanceRunning, 0);
            }
        });
    }

    private async Task RunMaintenanceAsync()
    {
        var now = TimeProvider.GetUtcNow();
        var staleConnections = _connections.Values
            .Where(connection => !IsSessionActive(connection.Claims) ||
                                 now - connection.LastSeenUtc > StaleConnectionTimeout ||
                                 connection.Socket.State is not WebSocketState.Open)
            .ToArray();
        foreach (var connection in staleConnections)
        {
            var sessionActive = IsSessionActive(connection.Claims);
            await CloseAndUnregisterAsync(
                connection,
                sessionActive ? WebSocketCloseStatus.EndpointUnavailable : WebSocketCloseStatus.PolicyViolation,
                sessionActive ? "heartbeat timeout" : "login session revoked");
        }

        foreach (var room in _callRooms.Values)
        {
            bool expired;
            lock (room.SyncRoot)
            {
                var lifetime = room.Participants.Count <= 1 ? TimeSpan.FromMinutes(10) : TimeSpan.FromHours(2);
                expired = now - room.LastActivityUtc > lifetime;
            }
            if (!expired || !_callRooms.TryRemove(new KeyValuePair<string, ChatCallRoom>(room.CallId, room)))
                continue;
            await SendToRoomAsync(room, ChatRealtimeProtocol.Create("call.ended", new
            {
                callId = room.CallId,
                reason = "expired"
            }), CancellationToken.None);
        }
    }

    private bool IsSessionActive(ChatRealtimeTicketClaims claims)
    {
        try { return SessionIsActive?.Invoke(claims) ?? true; }
        catch { return false; }
    }

    private async Task ObserveAsync(Task task, ChatRealtimeConnection connection)
    {
        try
        {
            await task;
        }
        catch
        {
            await CloseAndUnregisterAsync(connection, WebSocketCloseStatus.InternalServerError, "realtime handler failed");
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = item.IndexOf('=');
            if (separator <= 0)
                continue;
            var key = Uri.UnescapeDataString(item[..separator]);
            var value = Uri.UnescapeDataString(item[(separator + 1)..]);
            if (key.Length <= 64 && value.Length <= 512)
                result.TryAdd(key, value);
        }
        return result;
    }

    private static bool IsSafeIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 64 &&
        value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

    private static void TryAbort(WebSocket socket)
    {
        try { socket.Abort(); }
        catch (ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await _maintenanceTimer.DisposeAsync();
        var connections = _connections.Values.ToArray();
        foreach (var connection in connections)
            await CloseAndUnregisterAsync(connection, WebSocketCloseStatus.EndpointUnavailable, "server stopping");
    }
}
