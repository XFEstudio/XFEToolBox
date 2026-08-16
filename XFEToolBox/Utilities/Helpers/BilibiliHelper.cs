using System.Net.Http;
using XFEExtension.NetCore.XFETransform.Json;

namespace XFEToolBox.Client.Utilities.Helpers;

public sealed record BilibiliVideoInfo(string Bvid, string PictureUrl, string Title);

public static class BilibiliHelper
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static async Task<IReadOnlyList<BilibiliVideoInfo>> GetSeasonVideoList(
        string seasonId = "3641758",
        CancellationToken cancellationToken = default)
    {
        var requestUri = $"https://api.bilibili.com/x/polymer/web-space/seasons_archives_list?mid=200494622&season_id={Uri.EscapeDataString(seasonId)}&sort_reverse=false&page_size=30&page_num=1&web_location=333.1387";
        var responseContent = await HttpClient.GetStringAsync(requestUri, cancellationToken);
        XFEJsonNode root = responseContent;

        if (root["code"]?.GetInt32() != 0)
        {
            var message = root["message"]?.GetString() ?? "未知错误";
            throw new HttpRequestException($"Bilibili 接口返回异常：{message}");
        }

        var archives = root["data"]?["archives"];
        if (archives is null)
            return [];

        var videos = new List<BilibiliVideoInfo>();
        foreach (var archive in archives.EnumerateArray())
        {
            var bvid = archive["bvid"]?.GetString()?.Trim();
            var pictureUrl = archive["pic"]?.GetString()?.Trim();
            var title = archive["title"]?.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(bvid) ||
                string.IsNullOrWhiteSpace(pictureUrl) ||
                string.IsNullOrWhiteSpace(title))
                continue;

            videos.Add(new BilibiliVideoInfo(bvid, NormalizePictureUrl(pictureUrl), title));
        }

        return videos;
    }

    public static Task<byte[]> GetImageBytesAsync(string imageUrl, CancellationToken cancellationToken = default) =>
        HttpClient.GetByteArrayAsync(imageUrl, cancellationToken);

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
        client.DefaultRequestHeaders.Referrer = new Uri("https://space.bilibili.com/200494622/");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json,image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
        return client;
    }
}
