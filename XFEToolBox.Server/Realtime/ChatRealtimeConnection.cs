using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace XFEToolBox.Server.Realtime;

internal sealed class ChatRealtimeConnection : IAsyncDisposable
{
    private const int InboundCapacity = 128;
    private const int MaximumPendingSends = 128;
    private readonly Channel<string> _inbound = Channel.CreateBounded<string>(new BoundedChannelOptions(InboundCapacity)
    {
        AllowSynchronousContinuations = false,
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Queue<string> _recentEventOrder = new();
    private readonly HashSet<string> _recentEventIds = new(StringComparer.Ordinal);
    private long _lastSeenTimestamp;
    private long _rateWindow;
    private int _framesInWindow;
    private int _pendingSends;
    private int _closing;

    public ChatRealtimeConnection(WebSocket socket, ChatRealtimeTicketClaims claims, TimeProvider timeProvider)
    {
        Socket = socket;
        Claims = claims;
        TimeProvider = timeProvider;
        ConnectionId = Guid.NewGuid().ToString("N");
        Touch();
    }

    public string ConnectionId { get; }

    public WebSocket Socket { get; }

    public ChatRealtimeTicketClaims Claims { get; }

    public TimeProvider TimeProvider { get; }

    public DateTimeOffset LastSeenUtc => new(Interlocked.Read(ref _lastSeenTimestamp), TimeSpan.Zero);

    public CancellationToken LifetimeToken => _lifetime.Token;

    public void Touch() => Interlocked.Exchange(ref _lastSeenTimestamp, TimeProvider.GetUtcNow().UtcTicks);

    public bool TryConsumeFrameQuota(int maximumFramesPerTenSeconds)
    {
        var window = TimeProvider.GetUtcNow().ToUnixTimeSeconds() / 10;
        var observed = Interlocked.Read(ref _rateWindow);
        if (observed != window && Interlocked.CompareExchange(ref _rateWindow, window, observed) == observed)
            Interlocked.Exchange(ref _framesInWindow, 0);
        return Interlocked.Increment(ref _framesInWindow) <= maximumFramesPerTenSeconds;
    }

    public bool TryQueue(string message) =>
        Volatile.Read(ref _closing) == 0 && _inbound.Writer.TryWrite(message);

    public bool TryRememberEventId(string eventId)
    {
        // The inbound loop is single-reader, so this small bounded LRU needs no lock.
        if (!_recentEventIds.Add(eventId))
            return false;
        _recentEventOrder.Enqueue(eventId);
        while (_recentEventOrder.Count > 512)
            _recentEventIds.Remove(_recentEventOrder.Dequeue());
        return true;
    }

    public Task RunInboundLoopAsync(Func<ChatRealtimeConnection, string, CancellationToken, Task> handler) =>
        Task.Run(async () =>
        {
            try
            {
                await foreach (var message in _inbound.Reader.ReadAllAsync(_lifetime.Token))
                    await handler(this, message, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
        });

    public async Task<bool> SendTextAsync(string message, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _closing) != 0 || Socket.State != WebSocketState.Open)
            return false;
        if (Interlocked.Increment(ref _pendingSends) > MaximumPendingSends)
        {
            Interlocked.Decrement(ref _pendingSends);
            return false;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await _sendLock.WaitAsync(timeout.Token);
            try
            {
                if (Socket.State != WebSocketState.Open)
                    return false;
                var bytes = Encoding.UTF8.GetBytes(message);
                await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token);
                return true;
            }
            finally
            {
                _sendLock.Release();
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            return false;
        }
        finally
        {
            Interlocked.Decrement(ref _pendingSends);
        }
    }

    public async Task CloseAsync(WebSocketCloseStatus status, string description)
    {
        if (Interlocked.Exchange(ref _closing, 1) != 0)
            return;

        _inbound.Writer.TryComplete();
        _lifetime.Cancel();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await Socket.CloseOutputAsync(status, description, timeout.Token);
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            Socket.Abort();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync(WebSocketCloseStatus.NormalClosure, "connection disposed");
        _sendLock.Dispose();
        _lifetime.Dispose();
    }
}
