using System.Globalization;
using System.Net;
using XFEExtension.NetCore.CyberComm;

namespace XFEToolBox.Server.Core.Utilities;

/// <summary>
/// Applies optional HTTP metadata without assuming which CyberComm transport is serving the request.
/// </summary>
public static class ServerHttpResponseHelper
{
    public static void ConfigureDownload(
        CyberCommHttpResponse? response,
        HttpListenerResponse? legacyResponse,
        string contentType,
        long contentLength,
        string fileName,
        string etag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentOutOfRangeException.ThrowIfNegative(contentLength);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(etag);

        var contentDisposition = $"attachment; filename=\"{fileName}\"";
        var quotedEtag = $"\"{etag.Trim('\"')}\"";

        if (response is not null)
        {
            response.Headers["Content-Type"] = contentType;
            response.Headers["Content-Length"] = contentLength.ToString(CultureInfo.InvariantCulture);
            response.Headers["Content-Disposition"] = contentDisposition;
            response.Headers["ETag"] = quotedEtag;
        }

        if (legacyResponse is null) return;
        legacyResponse.ContentType = contentType;
        legacyResponse.ContentLength64 = contentLength;
        legacyResponse.Headers["Content-Disposition"] = contentDisposition;
        legacyResponse.Headers["ETag"] = quotedEtag;
    }
}
