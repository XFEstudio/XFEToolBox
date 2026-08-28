using System.Windows.Media;
using System.Windows.Media.Imaging;
using XFEToolBox.Client.Models;
using XFEToolBox.Client.Utilities;

namespace XFEToolBox.Client.Wpf.Test;

public static class RecentUsageIconCacheTests
{
    [Test]
    public static void RecentUsageIconsAreFrozenReusedAndBounded()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RecentUsageIconCache.Clear();
                var icon = CreateIcon(0x98, 0x98, 0xE7);
                Ensure(!icon.IsFrozen, "测试图标在写入缓存前不应被冻结。");

                RecentUsageIconCache.Remember(RecentUsageKind.Tool, "Code-Line-Counter", icon);
                Ensure(icon.IsFrozen, "最近使用图标写入缓存时没有被冻结。");
                Ensure(RecentUsageIconCache.TryGet(
                           RecentUsageKind.Tool,
                           "code-line-counter",
                           out var cachedIcon),
                    "主页没有按不区分大小写的工具 ID 找到会话图标。");
                Ensure(ReferenceEquals(icon, cachedIcon),
                    "主页没有直接复用工具箱已经解码完成的图标实例。");

                for (var index = 0; index < 40; index++)
                    RecentUsageIconCache.Remember(
                        RecentUsageKind.Software,
                        $"software-{index}",
                        CreateIcon((byte)index, 0x98, 0xE7));

                Ensure(!RecentUsageIconCache.TryGet(
                           RecentUsageKind.Tool,
                           "code-line-counter",
                           out _),
                    "图标缓存超过容量后没有淘汰最早的条目。");
                Ensure(RecentUsageIconCache.TryGet(
                           RecentUsageKind.Software,
                           "software-39",
                           out _),
                    "图标缓存错误淘汰了最新条目。");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                RecentUsageIconCache.Clear();
            }
        })
        {
            IsBackground = true,
            Name = "Recent usage icon cache test"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Ensure(thread.Join(TimeSpan.FromSeconds(10)), "最近使用图标缓存测试超时。");
        if (failure is not null)
            throw new InvalidOperationException($"最近使用图标缓存测试失败：{failure.Message}", failure);
    }

    private static BitmapSource CreateIcon(byte red, byte green, byte blue)
    {
        const int size = 4;
        var pixels = new byte[size * size * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = blue;
            pixels[index + 1] = green;
            pixels[index + 2] = red;
            pixels[index + 3] = 0xFF;
        }
        return BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
