namespace XFEToolBox.Server.Realtime;

internal sealed class ChatCallRoom(
    string callId,
    string creatorUserId,
    string targetType,
    string targetId,
    TimeProvider timeProvider)
{
    public object SyncRoot { get; } = new();

    public string CallId { get; } = callId;

    public string CreatorUserId { get; } = creatorUserId;

    public string TargetType { get; } = targetType;

    public string TargetId { get; } = targetId;

    public Dictionary<string, ChatCallParticipant> Participants { get; } = new(StringComparer.Ordinal);

    public DateTimeOffset LastActivityUtc { get; private set; } = timeProvider.GetUtcNow();

    public void Touch() => LastActivityUtc = timeProvider.GetUtcNow();
}

internal sealed record ChatCallParticipant(string UserId, string ConnectionId, string DisplayName);
