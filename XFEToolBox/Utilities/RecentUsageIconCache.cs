using System.Windows.Media;
using XFEToolBox.Client.Models;

namespace XFEToolBox.Client.Utilities;

/// <summary>
/// 保存工具箱和下载专区已经解码过的冻结图标，让主页最近使用卡片直接复用，
/// 避免页面切换时再次解析 Base64、SVG 或 GIF。
/// </summary>
public static class RecentUsageIconCache
{
    private const int MaximumEntries = 32;
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, ImageSource> Images = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> InsertionOrder = new();

    public static void Remember(RecentUsageKind kind, string targetId, ImageSource? image)
    {
        if (string.IsNullOrWhiteSpace(targetId) || image is null)
            return;

        if (!image.IsFrozen)
        {
            if (!image.CanFreeze)
                return;
            image.Freeze();
        }

        var key = CreateKey(kind, targetId);
        lock (SyncRoot)
        {
            if (Images.ContainsKey(key))
            {
                Images[key] = image;
                return;
            }

            Images.Add(key, image);
            InsertionOrder.Enqueue(key);
            while (Images.Count > MaximumEntries && InsertionOrder.TryDequeue(out var oldestKey))
                Images.Remove(oldestKey);
        }
    }

    public static bool TryGet(RecentUsageKind kind, string targetId, out ImageSource image)
    {
        lock (SyncRoot)
            return Images.TryGetValue(CreateKey(kind, targetId), out image!);
    }

    internal static void Clear()
    {
        lock (SyncRoot)
        {
            Images.Clear();
            InsertionOrder.Clear();
        }
    }

    private static string CreateKey(RecentUsageKind kind, string targetId) =>
        $"{kind}:{targetId.Trim()}";
}
