using System.Security.Cryptography;
using System.Text;

namespace XFEToolBox.Server.Realtime;

/// <summary>
/// Issues short-lived, single-use credentials that exchange an authenticated HTTP
/// session for a WebSocket connection. Only token digests are retained in memory.
/// </summary>
public sealed class ChatRealtimeTicketStore
{
    public const string RealtimeAudience = "chat.realtime";
    public const string RealtimeChannel = "chat";
    public const int DefaultMaximumOutstandingTickets = 10_000;
    public const int DefaultMaximumOutstandingTicketsPerUser = 5;

    private readonly object _syncRoot = new();
    private readonly Dictionary<string, ChatRealtimeTicketClaims> _tickets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _outstandingByUser = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ticketLifetime;
    private readonly int _maximumOutstandingTickets;
    private readonly int _maximumOutstandingTicketsPerUser;
    private long _issueCount;

    public ChatRealtimeTicketStore(
        TimeProvider? timeProvider = null,
        TimeSpan? ticketLifetime = null,
        int maximumOutstandingTickets = DefaultMaximumOutstandingTickets,
        int maximumOutstandingTicketsPerUser = DefaultMaximumOutstandingTicketsPerUser)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ticketLifetime = ticketLifetime ?? TimeSpan.FromSeconds(45);
        if (_ticketLifetime < TimeSpan.FromSeconds(30) || _ticketLifetime > TimeSpan.FromSeconds(60))
            throw new ArgumentOutOfRangeException(nameof(ticketLifetime), "Realtime tickets must live for 30 to 60 seconds.");
        if (maximumOutstandingTickets <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumOutstandingTickets));
        if (maximumOutstandingTicketsPerUser <= 0 ||
            maximumOutstandingTicketsPerUser > maximumOutstandingTickets)
            throw new ArgumentOutOfRangeException(nameof(maximumOutstandingTicketsPerUser));
        _maximumOutstandingTickets = maximumOutstandingTickets;
        _maximumOutstandingTicketsPerUser = maximumOutstandingTicketsPerUser;
    }

    public int OutstandingTicketCount
    {
        get { lock (_syncRoot) return _tickets.Count; }
    }

    public ChatRealtimeIssuedTicket Issue(
        string userId,
        string userName,
        string nickName,
        string deviceInfo,
        string audience = RealtimeAudience,
        string channel = RealtimeChannel,
        string sessionId = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        var now = _timeProvider.GetUtcNow();
        var expiresAtUtc = now.Add(_ticketLifetime);
        lock (_syncRoot)
        {
            var outstandingForUser = _outstandingByUser.GetValueOrDefault(userId);
            if ((++_issueCount & 63) == 0 ||
                _tickets.Count >= _maximumOutstandingTickets ||
                outstandingForUser >= _maximumOutstandingTicketsPerUser)
            {
                PruneExpiredUnsafe(now);
                outstandingForUser = _outstandingByUser.GetValueOrDefault(userId);
            }
            if (_tickets.Count >= _maximumOutstandingTickets)
                throw new ChatRealtimeTicketLimitException("实时通信待使用票据已达到服务器上限，请稍后重试。");
            if (outstandingForUser >= _maximumOutstandingTicketsPerUser)
                throw new ChatRealtimeTicketLimitException(
                    $"每个用户最多保留 {_maximumOutstandingTicketsPerUser} 张待使用实时票据，请先使用已有票据或稍后重试。");

            while (true)
            {
                var token = ToBase64Url(RandomNumberGenerator.GetBytes(32));
                var digest = ComputeDigest(token);
                var claims = new ChatRealtimeTicketClaims(
                    userId,
                    userName,
                    nickName,
                    deviceInfo,
                    audience,
                    channel,
                    now,
                    expiresAtUtc,
                    sessionId);
                if (!_tickets.TryAdd(digest, claims)) continue;
                _outstandingByUser[userId] = _outstandingByUser.GetValueOrDefault(userId) + 1;
                return new ChatRealtimeIssuedTicket(token, expiresAtUtc, audience, channel);
            }
        }
    }

    /// <summary>
    /// Consumes a ticket atomically. A malformed, expired, or audience-mismatched
    /// credential can never be retried.
    /// </summary>
    public bool TryConsume(
        string token,
        string expectedAudience,
        string expectedChannel,
        out ChatRealtimeTicketClaims claims)
    {
        claims = default!;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256)
            return false;

        lock (_syncRoot)
        {
            var digest = ComputeDigest(token);
            if (!_tickets.Remove(digest, out var candidate))
                return false;
            DecrementUserCountUnsafe(candidate.UserId);

            var now = _timeProvider.GetUtcNow();
            if (candidate.ExpiresAtUtc <= now ||
                !string.Equals(candidate.Audience, expectedAudience, StringComparison.Ordinal) ||
                !string.Equals(candidate.Channel, expectedChannel, StringComparison.Ordinal))
                return false;

            claims = candidate;
            return true;
        }
    }

    private void PruneExpiredUnsafe(DateTimeOffset now)
    {
        foreach (var pair in _tickets.Where(pair => pair.Value.ExpiresAtUtc <= now).ToArray())
        {
            if (!_tickets.Remove(pair.Key)) continue;
            DecrementUserCountUnsafe(pair.Value.UserId);
        }
    }

    private void DecrementUserCountUnsafe(string userId)
    {
        if (!_outstandingByUser.TryGetValue(userId, out var count)) return;
        if (count <= 1)
            _outstandingByUser.Remove(userId);
        else
            _outstandingByUser[userId] = count - 1;
    }

    private static string ComputeDigest(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string ToBase64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class ChatRealtimeTicketLimitException(string message) : Exception(message);

public sealed record ChatRealtimeIssuedTicket(
    string Ticket,
    DateTimeOffset ExpiresAtUtc,
    string Audience,
    string Channel);

public sealed record ChatRealtimeTicketClaims(
    string UserId,
    string UserName,
    string NickName,
    string DeviceInfo,
    string Audience,
    string Channel,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string SessionId = "");
