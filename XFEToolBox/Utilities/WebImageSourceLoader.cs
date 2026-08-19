using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;
using SharpVectors.Converters;
using SharpVectors.Dom;
using SharpVectors.Renderers.Wpf;

namespace XFEToolBox.Client.Utilities;

/// <summary>
/// 将 HTTP(S) 或 data:image 地址统一解码为可跨线程使用的 WPF 图像。
/// 支持 WIC 位图格式（包括 ICO、静态或动态 GIF）以及 SVG/SVGZ。
/// GIF 会保留原始解码器和全部帧，供 WpfAnimatedGif 在界面中播放。
/// </summary>
public static class WebImageSourceLoader
{
    public const int MaximumImageBytes = 4 * 1024 * 1024;

    private const int MaximumCachedImages = 256;
    private static readonly SemaphoreSlim DecodeGate = new(Math.Clamp(Environment.ProcessorCount / 2, 1, 4));
    private static readonly ConcurrentDictionary<string, Lazy<Task<ImageSource?>>> Cache =
        new(StringComparer.Ordinal);
    private static readonly HttpClient ImageClient = CreateImageClient();

    /// <summary>
    /// 异步读取网络地址或 data:image URI。失败会抛出可诊断异常，不缓存失败结果。
    /// </summary>
    public static async Task<ImageSource?> LoadAsync(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();
        var displayDispatcher = GetDisplayDispatcher();
        // data URI 可能包含数 MB 文本，计算缓存键也不能占用 UI 线程。
        var cacheKey = value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
            ? await Task.Run(() => CreateCacheKey(value)).ConfigureAwait(false)
            : value;
        if (Cache.Count >= MaximumCachedImages && !Cache.ContainsKey(cacheKey))
            Cache.Clear();

        var lazy = Cache.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<ImageSource?>>(
                () => LoadCoreAsync(value, displayDispatcher),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            var result = await lazy.Value.ConfigureAwait(false);
            if (result is null)
                Cache.TryRemove(new KeyValuePair<string, Lazy<Task<ImageSource?>>>(cacheKey, lazy));
            return result;
        }
        catch
        {
            Cache.TryRemove(new KeyValuePair<string, Lazy<Task<ImageSource?>>>(cacheKey, lazy));
            throw;
        }
    }

    /// <summary>
    /// 从已下载的内容解码图像；公开此入口以供缓存层和测试复用。
    /// </summary>
    public static ImageSource Decode(byte[] bytes, string? mediaType = null, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
            throw new InvalidDataException("图标内容为空。");
        if (bytes.Length > MaximumImageBytes)
            throw new InvalidDataException($"图标超过 {MaximumImageBytes / 1024 / 1024} MB 限制。");

        bytes = DecompressSvgzIfNeeded(bytes, mediaType, sourceName);
        return IsSvg(bytes, mediaType, sourceName)
            ? DecodeSvg(bytes)
            : DecodeBitmap(bytes);
    }

    /// <summary>
    /// 在合适的线程中完成图像解码：SVG 与普通位图在受限后台线程执行，
    /// 动态 GIF 则在显示 Dispatcher 上创建，以保留可用的多帧解码器。
    /// </summary>
    public static Task<ImageSource> DecodeAsync(byte[] bytes, string? mediaType = null, string? sourceName = null) =>
        DecodeForDisplayAsync(bytes, mediaType, sourceName, GetDisplayDispatcher());

    private static async Task<ImageSource?> LoadCoreAsync(string value, Dispatcher? displayDispatcher)
    {
        if (value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            var decodedData = await Task.Run(() => DecodeDataUri(value)).ConfigureAwait(false);
            return await DecodeForDisplayAsync(
                    decodedData.Bytes,
                    decodedData.MediaType,
                    decodedData.MediaType,
                    displayDispatcher)
                .ConfigureAwait(false);
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("图标地址必须是 HTTP(S) 地址或 data:image URI。", nameof(value));

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/svg+xml"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*", 0.9));
        using var response = await ImageClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength > MaximumImageBytes)
            throw new InvalidDataException($"图标超过 {MaximumImageBytes / 1024 / 1024} MB 限制。");

        await using var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var bytes = await ReadLimitedAsync(responseStream).ConfigureAwait(false);
        return await DecodeForDisplayAsync(
                bytes,
                response.Content.Headers.ContentType?.MediaType,
                uri.AbsolutePath,
                displayDispatcher)
            .ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream)
    {
        using var output = new MemoryStream();
        var buffer = new byte[32 * 1024];
        while (true)
        {
            var count = await stream.ReadAsync(buffer).ConfigureAwait(false);
            if (count == 0)
                break;
            if (output.Length + count > MaximumImageBytes)
                throw new InvalidDataException($"图标超过 {MaximumImageBytes / 1024 / 1024} MB 限制。");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static (byte[] Bytes, string MediaType) DecodeDataUri(string value)
    {
        var separator = value.IndexOf(',');
        if (separator <= 5)
            throw new FormatException("data:image URI 缺少内容分隔符。");

        var header = value[5..separator];
        var segments = header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var mediaType = segments.FirstOrDefault() ?? string.Empty;
        if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("data URI 不是图像类型。");

        var payload = value[(separator + 1)..];
        var isBase64 = segments.Skip(1).Any(segment => segment.Equals("base64", StringComparison.OrdinalIgnoreCase));
        if (isBase64)
        {
            if (payload.Length > (MaximumImageBytes + 2L) / 3 * 4 + 8)
                throw new InvalidDataException($"图标超过 {MaximumImageBytes / 1024 / 1024} MB 限制。");
            return (Convert.FromBase64String(payload), mediaType);
        }

        if (payload.Length > MaximumImageBytes * 3L)
            throw new InvalidDataException($"图标超过 {MaximumImageBytes / 1024 / 1024} MB 限制。");
        return (Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload)), mediaType);
    }

    private static ImageSource DecodeBitmap(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var frame = decoder.Frames
                .OrderByDescending(candidate => (long)candidate.PixelWidth * candidate.PixelHeight)
                .FirstOrDefault()
                ?? throw new InvalidDataException("图标中没有可显示的位图帧。");
            if (frame.CanFreeze)
                frame.Freeze();
            return frame;
        }
        catch (Exception exception) when (exception is NotSupportedException or FileFormatException or ArgumentException)
        {
            throw new InvalidDataException("不支持的图标格式或图标内容已损坏。", exception);
        }
    }

    private static ImageSource DecodeSvg(byte[] bytes)
    {
        var drawingSettings = new WpfDrawingSettings
        {
            IncludeRuntime = false,
            // 软件图标无需将文字逐字转成路径，也无需在首次加载时重写全部路径。
            // 关闭这两项可显著降低复杂 SVG 的解析与内存成本。
            TextAsGeometry = false,
            OptimizePath = false,
            CanUseBitmap = false,
            ExternalResourcesAccessMode = ExternalResourcesAccessModes.Ignore
        };

        using var stream = new MemoryStream(bytes, writable: false);
        var xmlSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumImageBytes
        };
        using var xmlReader = XmlReader.Create(stream, xmlSettings);
        using var svgReader = new FileSvgReader(drawingSettings, isEmbedded: true);
        var drawing = svgReader.Read(xmlReader)
                      ?? throw new InvalidDataException("SVG 图标没有生成可显示内容。");
        if (drawing.CanFreeze)
            drawing.Freeze();

        var image = new DrawingImage(drawing);
        if (image.CanFreeze)
            image.Freeze();
        return image;
    }

    private static async Task<ImageSource> RunDecodeAsync(Func<ImageSource> decode)
    {
        await DecodeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(decode).ConfigureAwait(false);
        }
        finally
        {
            DecodeGate.Release();
        }
    }

    private static async Task<ImageSource> DecodeForDisplayAsync(
        byte[] bytes,
        string? mediaType,
        string? sourceName,
        Dispatcher? displayDispatcher)
    {
        if (IsGif(bytes, mediaType, sourceName) &&
            displayDispatcher is { HasShutdownStarted: false, HasShutdownFinished: false })
        {
            if (displayDispatcher.CheckAccess())
                return Decode(bytes, mediaType, sourceName);

            return await displayDispatcher
                .InvokeAsync(
                    () => Decode(bytes, mediaType, sourceName),
                    DispatcherPriority.Background)
                .Task
                .ConfigureAwait(false);
        }

        return await RunDecodeAsync(() => Decode(bytes, mediaType, sourceName)).ConfigureAwait(false);
    }

    private static bool IsGif(byte[] bytes, string? mediaType, string? sourceName)
    {
        if (string.Equals(mediaType, "image/gif", StringComparison.OrdinalIgnoreCase) ||
            HasExtension(sourceName, ".gif"))
            return true;

        return bytes.Length >= 6 &&
               bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' &&
               bytes[3] == (byte)'8' && (bytes[4] == (byte)'7' || bytes[4] == (byte)'9') &&
               bytes[5] == (byte)'a';
    }

    private static Dispatcher? GetDisplayDispatcher()
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            return Dispatcher.FromThread(Thread.CurrentThread) ?? Dispatcher.CurrentDispatcher;
        return Application.Current?.Dispatcher;
    }

    private static bool IsSvg(byte[] bytes, string? mediaType, string? sourceName)
    {
        if (string.Equals(mediaType, "image/svg+xml", StringComparison.OrdinalIgnoreCase) ||
            HasExtension(sourceName, ".svg") || HasExtension(sourceName, ".svgz"))
            return true;

        var prefixLength = Math.Min(bytes.Length, 4096);
        var prefix = Encoding.UTF8.GetString(bytes, 0, prefixLength)
            .TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return prefix.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] DecompressSvgzIfNeeded(byte[] bytes, string? mediaType, string? sourceName)
    {
        var isGzip = bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B;
        if (!isGzip)
            return bytes;
        if (!string.Equals(mediaType, "image/svg+xml", StringComparison.OrdinalIgnoreCase) &&
            !HasExtension(sourceName, ".svgz"))
            return bytes;

        using var input = new MemoryStream(bytes, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        return ReadLimited(gzip);
    }

    private static byte[] ReadLimited(Stream stream)
    {
        using var output = new MemoryStream();
        var buffer = new byte[32 * 1024];
        while (true)
        {
            var count = stream.Read(buffer, 0, buffer.Length);
            if (count == 0)
                break;
            if (output.Length + count > MaximumImageBytes)
                throw new InvalidDataException($"解压后的 SVG 超过 {MaximumImageBytes / 1024 / 1024} MB 限制。");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static bool HasExtension(string? sourceName, string extension)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
            return false;
        var path = sourceName.Split('?', '#')[0];
        return path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateCacheKey(string value)
    {
        if (!value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            return value;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"data:{Convert.ToHexString(hash)}";
    }

    private static HttpClient CreateImageClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(8)
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("XFEToolBox/1.1");
        return client;
    }
}
