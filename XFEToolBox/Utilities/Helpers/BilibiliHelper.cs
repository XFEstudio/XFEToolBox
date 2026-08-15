using System.Net.Http;
using XFEExtension.NetCore.XFETransform.JsonConverter;

namespace XFEToolBox.Client.Utilities.Helpers;

public static class BilibiliHelper
{
    public static async Task<List<Dictionary<string, ValueNode>>> GetSeasonVideoList(string seasonId = "3641758")
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36 Edg/145.0.0.0");
        QueryableJsonNode jsonNode = await client.GetStringAsync($"https://api.bilibili.com/x/polymer/web-space/seasons_archives_list?mid=200494622&season_id={seasonId}&sort_reverse=false&page_size=30&page_num=1&web_location=333.1387");
        if (jsonNode["data"]?["archives"]?["package:list", "aid", "bvid", "pic", "title"].PackageInListObject() is List<Dictionary<string, ValueNode>> subJsonNodes)
            return subJsonNodes;
        return [];
    }
}
