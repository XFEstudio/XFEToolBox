using System.Text.Json;

namespace XFEToolBox.Server.Realtime;

internal sealed record ChatRelayAudioFrame(long Sequence, int SampleRate, string PcmBase64)
{
    public const int RequiredSampleRate = 16_000;
    public const int RequiredPcmByteCount = 640;
    public const int MaximumBase64Length = 1024;

    public static bool TryParse(JsonElement payload, out ChatRelayAudioFrame frame)
    {
        frame = default!;
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("sequence", out var sequenceProperty) ||
            !sequenceProperty.TryGetInt64(out var sequence) ||
            sequence < 0 ||
            !payload.TryGetProperty("sampleRate", out var sampleRateProperty) ||
            !sampleRateProperty.TryGetInt32(out var sampleRate) ||
            sampleRate != RequiredSampleRate ||
            !payload.TryGetProperty("pcmBase64", out var pcmProperty) ||
            pcmProperty.ValueKind != JsonValueKind.String)
            return false;

        var pcmBase64 = pcmProperty.GetString();
        if (string.IsNullOrWhiteSpace(pcmBase64) || pcmBase64.Length > MaximumBase64Length)
            return false;
        Span<byte> decoded = stackalloc byte[RequiredPcmByteCount];
        if (!Convert.TryFromBase64String(pcmBase64, decoded, out var bytesWritten) ||
            bytesWritten != RequiredPcmByteCount)
            return false;

        frame = new ChatRelayAudioFrame(sequence, sampleRate, pcmBase64);
        return true;
    }
}
