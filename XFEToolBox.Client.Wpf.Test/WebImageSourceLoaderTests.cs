using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfAnimatedGif;
using XFEToolBox.Client.Utilities;

namespace XFEToolBox.Client.Wpf.Test;

public static class WebImageSourceLoaderTests
{
    private const string SvgMarkup =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 32 32\"><rect width=\"32\" height=\"32\" rx=\"6\" fill=\"#9898e7\"/><path d=\"M8 16h16\" stroke=\"white\" stroke-width=\"3\"/></svg>";

    [Test]
    public static void WebImageLoaderDecodesSvgIcoAndSvgDataUris()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var svgImage = WebImageSourceLoader.Decode(
                    Encoding.UTF8.GetBytes(SvgMarkup),
                    "application/octet-stream",
                    "icon?id=42");
                Ensure(svgImage is DrawingImage { IsFrozen: true },
                    "没有后缀且类型不准确的 SVG 没有被内容识别并转换成 WPF DrawingImage。");

                var icoPath = Path.Combine(AppContext.BaseDirectory, "TestAssets", "XFEToolBoxIcon.ico");
                var icoImage = WebImageSourceLoader.Decode(
                    File.ReadAllBytes(icoPath),
                    "image/x-icon",
                    "favicon.ico");
                Ensure(icoImage is DrawingImage
                       {
                           IsFrozen: true,
                           Drawing: ImageDrawing
                           {
                               ImageSource: BitmapSource { PixelWidth: >= 32 }
                           }
                       },
                    "ICO 没有选择清晰的位图帧并包装成可安全跨线程显示的图像。");

                var dataUri = "data:image/svg+xml," + Uri.EscapeDataString(SvgMarkup);
                var dataImage = AwaitWithDispatcher(WebImageSourceLoader.LoadAsync(dataUri), TimeSpan.FromSeconds(10));
                Ensure(dataImage is DrawingImage { IsFrozen: true },
                    "非 Base64 的 SVG data URI 没有被正确解码。");

                var gifBytes = CreateAnimatedGif();
                var gifImage = WebImageSourceLoader.Decode(
                    gifBytes,
                    "application/octet-stream",
                    "icon?format=animated");
                Ensure(gifImage is BitmapFrame { Decoder: GifBitmapDecoder gifDecoder, IsFrozen: true } &&
                       gifDecoder.Frames.Count == 2,
                    "动态 GIF 没有被内容识别，或多帧信息在解码后丢失。");

                var gifDataUri = "data:image/gif;base64," + Convert.ToBase64String(gifBytes);
                var gifDataImage = AwaitWithDispatcher(
                    WebImageSourceLoader.LoadAsync(gifDataUri),
                    TimeSpan.FromSeconds(10));
                Ensure(gifDataImage is BitmapFrame { Decoder: GifBitmapDecoder dataGifDecoder } &&
                       dataGifDecoder.Frames.Count == 2,
                    "Base64 GIF data URI 没有保留动画帧。");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Ensure(thread.Join(TimeSpan.FromSeconds(15)), "网络图标格式测试超时。");
        if (failure is not null)
            throw new InvalidOperationException($"网络图标格式加载测试失败：{failure.Message}", failure);
    }

    [Test]
    public static void WebImageLoaderRejectsOversizedAndNonImageContent()
    {
        EnsureThrows<InvalidDataException>(() =>
            WebImageSourceLoader.Decode(new byte[WebImageSourceLoader.MaximumImageBytes + 1], "image/png"));
        EnsureThrows<InvalidDataException>(() =>
            WebImageSourceLoader.Decode(Encoding.UTF8.GetBytes("not an image"), "application/octet-stream"));
    }

    [Test]
    public static void BackgroundDecodedPngCanBeDisplayedByAnimatedImageBehavior()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            DispatcherUnhandledExceptionEventHandler? dispatcherFailureHandler = null;
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                Exception? dispatcherFailure = null;
                dispatcherFailureHandler = (_, eventArgs) =>
                {
                    dispatcherFailure = eventArgs.Exception;
                    eventArgs.Handled = true;
                };
                dispatcher.UnhandledException += dispatcherFailureHandler;

                var dataUri = "data:image/png;base64," + Convert.ToBase64String(CreatePng());
                var source = AwaitWithDispatcher(
                    WebImageSourceLoader.LoadAsync(dataUri),
                    TimeSpan.FromSeconds(10));
                var image = new Image { Width = 32, Height = 32 };
                var fallback = WebImageSourceLoader.Decode(
                    Encoding.UTF8.GetBytes(SvgMarkup),
                    "image/svg+xml",
                    "fallback.svg");
                ImageBehavior.SetAnimatedSource(image, fallback);
                window = new Window
                {
                    Width = 80,
                    Height = 80,
                    Left = -10_000,
                    Top = -10_000,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Content = image
                };

                window.Show();
                PumpDispatcher(dispatcher);
                window.UpdateLayout();
                Ensure(ReferenceEquals(image.Source, fallback),
                    "主页卡片的初始回退图标没有完成显示。");

                // 模拟 RecentUsageCardViewModel 在卡片 Loaded 后异步换成目录中的真实工具图标。
                ImageBehavior.SetAnimatedSource(image, source);
                PumpDispatcher(dispatcher);
                window.UpdateLayout();

                Ensure(dispatcherFailure is null,
                    $"后台解码的静态 PNG 进入动画兼容显示路径时触发 UI 异常：{dispatcherFailure}");
                Ensure(ReferenceEquals(image.Source, source),
                    "后台解码的静态 PNG 没有替换主页卡片的回退图标。");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                window?.Close();
                if (dispatcherFailureHandler is not null)
                    Dispatcher.CurrentDispatcher.UnhandledException -= dispatcherFailureHandler;
            }
        })
        {
            IsBackground = true,
            Name = "Background decoded recent icon display test"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Ensure(thread.Join(TimeSpan.FromSeconds(15)), "后台解码的最近使用图标显示测试超时。");
        if (failure is not null)
            throw new InvalidOperationException($"后台解码的最近使用图标无法显示：{failure.Message}", failure);
    }

    [Test]
    public static void LargeSvgLoadingStaysOffTheUiThreadAndSharesItsCachedResult()
    {
        var dataUri = "data:image/svg+xml;base64," +
                      Convert.ToBase64String(Encoding.UTF8.GetBytes(CreateComplexSvg(6000)));
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var invocationTimer = Stopwatch.StartNew();
                var firstLoad = WebImageSourceLoader.LoadAsync(dataUri);
                var concurrentLoad = WebImageSourceLoader.LoadAsync(dataUri);
                invocationTimer.Stop();

                // 如果解析仍在调用线程同步执行，这里会在大型 SVG 解析完之后才有机会投递 UI 消息。
                Ensure(invocationTimer.Elapsed < TimeSpan.FromMilliseconds(150),
                    $"大型 SVG 的 LoadAsync 调用阻塞了 UI {invocationTimer.Elapsed.TotalMilliseconds:F1} ms。");

                var uiPulseReceived = false;
                dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => uiPulseReceived = true));

                var allLoads = Task.WhenAll(firstLoad, concurrentLoad);
                var frame = new DispatcherFrame();
                _ = Task.Run(async () =>
                {
                    await Task.WhenAny(allLoads, Task.Delay(TimeSpan.FromSeconds(15)));
                    await dispatcher.BeginInvoke(DispatcherPriority.Background,
                        new Action(() => frame.Continue = false));
                });
                Dispatcher.PushFrame(frame);

                Ensure(uiPulseReceived, "大型 SVG 解析期间 WPF Dispatcher 未能处理界面消息。");
                Ensure(allLoads.IsCompletedSuccessfully, "大型 SVG 没有在 15 秒内完成后台解析。\n" +
                                                        allLoads.Exception?.GetBaseException().Message);

                var images = allLoads.GetAwaiter().GetResult();
                Ensure(images[0] is DrawingImage { IsFrozen: true },
                    "后台解析结果不是可跨线程使用的冻结 DrawingImage。");
                Ensure(ReferenceEquals(images[0], images[1]),
                    "相同 SVG 的并发请求没有复用同一个解析任务和缓存结果。");

                var cacheTimer = Stopwatch.StartNew();
                var cachedImage = WebImageSourceLoader.LoadAsync(dataUri).GetAwaiter().GetResult();
                cacheTimer.Stop();
                Ensure(ReferenceEquals(images[0], cachedImage), "大型 SVG 的后续请求没有命中缓存。");
                Ensure(cacheTimer.Elapsed < TimeSpan.FromMilliseconds(250),
                    $"大型 SVG 缓存读取耗时异常：{cacheTimer.Elapsed.TotalMilliseconds:F1} ms。");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
            Name = "WebImageSourceLoader SVG responsiveness test"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Ensure(thread.Join(TimeSpan.FromSeconds(20)), "大型 SVG UI 响应性测试超时。");
        if (failure is not null)
            throw new InvalidOperationException($"大型 SVG 后台解析与缓存测试失败：{failure.Message}", failure);
    }

    private static string CreateComplexSvg(int elementCount)
    {
        var svg = new StringBuilder(elementCount * 64);
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 512 512\">");
        for (var index = 0; index < elementCount; index++)
        {
            var x = index % 512;
            var y = index * 17 % 512;
            svg.Append("<path d=\"M")
                .Append(x).Append(' ').Append(y)
                .Append("h8v8h-8z\" fill=\"#9898e7\" opacity=\".75\"/>");
        }
        return svg.Append("</svg>").ToString();
    }

    private static T AwaitWithDispatcher<T>(Task<T> task, TimeSpan timeout)
    {
        if (!task.IsCompleted)
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var frame = new DispatcherFrame();
            _ = Task.Run(async () =>
            {
                await Task.WhenAny(task, Task.Delay(timeout));
                await dispatcher.BeginInvoke(DispatcherPriority.Background,
                    new Action(() => frame.Continue = false));
            });
            Dispatcher.PushFrame(frame);
        }

        Ensure(task.IsCompleted, $"等待后台图像解析超过 {timeout.TotalSeconds:F0} 秒。");
        return task.GetAwaiter().GetResult();
    }

    private static byte[] CreateAnimatedGif()
    {
        var encoder = new GifBitmapEncoder();
        encoder.Frames.Add(CreateSolidFrame(0xE7, 0x98, 0x98));
        encoder.Frames.Add(CreateSolidFrame(0x98, 0xE7, 0xB0));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private static byte[] CreatePng()
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(CreateSolidFrame(0x98, 0x98, 0xE7));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private static void PumpDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        _ = dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static BitmapFrame CreateSolidFrame(byte red, byte green, byte blue)
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

        var bitmap = BitmapSource.Create(
            size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        return BitmapFrame.Create(bitmap);
    }

    private static void EnsureThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"预期抛出 {typeof(TException).Name}，但操作成功了。");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
