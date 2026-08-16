using System.Net.Http;
using XFEExtension.NetCore.XFETransform.Json;

namespace XFEToolBox.Client.Utilities.Helpers;

public sealed record BilibiliVideoInfo(string Bvid, string PictureUrl, string Title);

public static class BilibiliHelper
{
    public const string CreatorMid = "200494622";
    public const string CSharpSeasonId = "3641758";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static Task<IReadOnlyList<BilibiliVideoInfo>> GetPopularVideoListAsync(
        int pageSize = 6,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);
        var requestUri = $"https://api.bilibili.com/x/web-interface/popular?pn=1&ps={pageSize}";
        return GetVideoListAsync(requestUri, static root => root["data"]?["list"], cancellationToken);
    }

    public static Task<IReadOnlyList<BilibiliVideoInfo>> GetLatestCreatorVideoListAsync(
        int pageSize = 6,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 50);
        var requestUri = $"https://api.bilibili.com/x/space/arc/list?mid={CreatorMid}&pn=1&ps={pageSize}&order=pubdate";
        return GetVideoListAsync(requestUri, static root => root["data"]?["archives"], cancellationToken);
    }

    public static Task<IReadOnlyList<BilibiliVideoInfo>> GetSeasonVideoListAsync(
        string seasonId = CSharpSeasonId,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 30);
        var requestUri = $"https://api.bilibili.com/x/polymer/web-space/seasons_archives_list?mid={CreatorMid}&season_id={Uri.EscapeDataString(seasonId)}&sort_reverse=false&page_size={pageSize}&page_num=1&web_location=333.1387";
        return GetVideoListAsync(requestUri, static root => root["data"]?["archives"], cancellationToken);
    }

    public static Task<byte[]> GetImageBytesAsync(string imageUrl, CancellationToken cancellationToken = default) =>
        HttpClient.GetByteArrayAsync(imageUrl, cancellationToken);

    private static async Task<IReadOnlyList<BilibiliVideoInfo>> GetVideoListAsync(
        string requestUri,
        Func<XFEJsonNode, XFEJsonNode?> selectItems,
        CancellationToken cancellationToken)
    {
        var responseContent = await HttpClient.GetStringAsync(requestUri, cancellationToken);
        XFEJsonNode root = responseContent;

        if (root["code"]?.GetInt32() != 0)
        {
            var message = root["message"]?.GetString() ?? "未知错误";
            throw new HttpRequestException($"Bilibili 接口返回异常：{message}");
        }

        var items = selectItems(root);
        if (items is null)
            return [];

        var videos = new List<BilibiliVideoInfo>();
        foreach (var item in items.EnumerateArray())
        {
            var bvid = item["bvid"]?.GetString()?.Trim();
            var pictureUrl = item["pic"]?.GetString()?.Trim();
            var title = item["title"]?.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(bvid) ||
                string.IsNullOrWhiteSpace(pictureUrl) ||
                string.IsNullOrWhiteSpace(title))
                continue;

            videos.Add(new BilibiliVideoInfo(bvid, NormalizePictureUrl(pictureUrl), title));
        }

        return videos;
    }

    private static string NormalizePictureUrl(string pictureUrl)
    {
        if (pictureUrl.StartsWith("//", StringComparison.Ordinal))
            return $"https:{pictureUrl}";
        if (pictureUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return $"https://{pictureUrl[7..]}";
        return pictureUrl;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/145.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Referrer = new Uri($"https://space.bilibili.com/{CreatorMid}/");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json,image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
        return client;
    }
}
