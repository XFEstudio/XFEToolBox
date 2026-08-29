using System.Text.Json;
using System.Text.Json.Serialization;

namespace XFEToolBox.Server.Realtime;

public sealed class ChatRealtimeEnvelope
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public string EventId { get; init; } = Guid.NewGuid().ToString("N");

    public string Type { get; init; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public string? ConversationId { get; init; }

    public string? ActorUserId { get; init; }

    public JsonElement Payload { get; init; }

    [JsonIgnore]
    public bool HasPayload => Payload.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null;
}

internal static class ChatRealtimeProtocol
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static ChatRealtimeEnvelope Create(
        string type,
        object? payload = null,
        string? actorUserId = null,
        string? conversationId = null) => new()
    {
        Type = type,
        ActorUserId = actorUserId,
        ConversationId = conversationId,
        Payload = JsonSerializer.SerializeToElement(payload ?? new { }, JsonOptions)
    };

    public static bool TryParse(string json, out ChatRealtimeEnvelope envelope, out string error)
    {
        envelope = default!;
        error = string.Empty;
        try
        {
            var parsed = JsonSerializer.Deserialize<ChatRealtimeEnvelope>(json, JsonOptions);
            if (parsed is null)
            {
                error = "消息体为空。";
                return false;
            }

            if (parsed.Version != ChatRealtimeEnvelope.CurrentVersion)
            {
                error = "不支持的实时协议版本。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(parsed.Type) || parsed.Type.Length > 64 ||
                !parsed.Type.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_'))
            {
                error = "事件类型无效。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(parsed.EventId) || parsed.EventId.Length > 96)
            {
                error = "事件标识无效。";
                return false;
            }

            envelope = parsed;
            return true;
        }
        catch (JsonException)
        {
            error = "消息不是有效的 JSON。";
            return false;
        }
    }

    public static bool TryGetRequiredString(JsonElement payload, string propertyName, out string value)
    {
        value = string.Empty;
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
            return false;

        value = property.GetString()?.Trim() ?? string.Empty;
        return value.Length > 0;
    }

    public static string? GetOptionalString(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
            return null;
        var value = property.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
