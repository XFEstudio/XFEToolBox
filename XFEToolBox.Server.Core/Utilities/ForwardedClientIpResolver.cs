using System.Net;

namespace XFEToolBox.Server.Core.Utilities;

/// <summary>
/// Resolves the client address forwarded by a trusted reverse proxy.
/// </summary>
public static class ForwardedClientIpResolver
{
    /// <summary>
    /// Resolves an EdgeOne client address, falling back to the transport peer address.
    /// The origin must only accept traffic from the trusted proxy before forwarded
    /// headers can be treated as authoritative.
    /// </summary>
    public static string Resolve(
        string connectionIp,
        string? edgeOneConnectingIp,
        string? forwardedFor)
    {
        if (TryNormalize(edgeOneConnectingIp, out var clientIp))
            return clientIp;

        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var addresses = forwardedFor.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (addresses.Length > 0 && TryNormalize(addresses[^1], out clientIp))
                return clientIp;
        }

        return TryNormalize(connectionIp, out clientIp) ? clientIp : connectionIp;
    }

    private static bool TryNormalize(string? value, out string address)
    {
        if (IPAddress.TryParse(value?.Trim(), out var parsed))
        {
            address = parsed.IsIPv4MappedToIPv6
                ? parsed.MapToIPv4().ToString()
                : parsed.ToString();
            return true;
        }

        address = string.Empty;
        return false;
    }
}
