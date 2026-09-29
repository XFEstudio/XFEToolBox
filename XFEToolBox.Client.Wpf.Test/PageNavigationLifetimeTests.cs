using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using XFEToolBox.Client.Views.Pages;
using XFEToolBox.Core.Tools;
using XFEToolBox.WpfCore.Controls;

namespace XFEToolBox.Client.Wpf.Test;

public static class PageNavigationLifetimeTests
{
    [Test]
    [NonParallel]
    [Isolated]
    public static void UnchangedToolCatalogReusesRowsWithoutSerializingIcons() => RunSta(() =>
    {
        using var application = new TestApplication();
        var pending = new TaskCompletionSource<ToolPackageSummary[]>();
        var page = new ToolBoxPage((_, _) => pending.Task);
        var original = Summary();
        page.ApplyTools([original]);
        var rows = page.ToolCards.ItemsSource;
        for (var index = 0; index < 100; index++)
            page.ApplyTools([Summary()]);
        Ensure(ReferenceEquals(rows, page.ToolCards.ItemsSource), "未变化的工具目录仍重建了虚拟化列表。");

        // Comparing a catalog snapshot must not serialize its potentially megabyte-sized data URI.
        var left = Summary(icon: new string('a', 100_000));
        var right = Summary(icon: new string('a', 100_000));
        ToolBoxPage.ToolSummariesEquivalent(left, right);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 100; index++)
            Ensure(ToolBoxPage.ToolSummariesEquivalent(left, right), "相同的工具摘要比较失败。");
        Ensure(GC.GetAllocatedBytesForCurrentThread() - before < 100_000,
            "工具摘要比较仍分配了与图标内容大小成倍增长的内存。");

        page.ApplyTools([Summary(name: "已更新的工具")]);
        Ensure(!ReferenceEquals(rows, page.ToolCards.ItemsSource), "目录变化没有刷新工具卡片。");
        page.ApplyTools([]);
        Ensure(page.ToolCards.Items.Count == 0, "目录移除后仍显示旧工具。");
    });

    [Test]
    [NonParallel]
    [Isolated]
    public static void ToolCatalogResponseAfterLeavingPageDoesNotRebuildHiddenList() => RunSta(() =>
    {
        using var application = new TestApplication();
        var pending = new TaskCompletionSource<ToolPackageSummary[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var page = new ToolBoxPage((_, _) => { calls++; return pending.Task; });
        SetField(page, "_cacheLoaded", true);
        SetField(page, "_hasCachedCatalog", true);
        SetField(page, "_cachedCatalog", new[] { Summary() });
        var host = new Frame { NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden, Content = page };
        using var window = new TestWindow(host);
        PumpUntil(() => calls == 1 && page.IsLoaded);
        var rows = page.ToolCards.ItemsSource;
        var home = new Page { Content = new TextBlock { Text = "主页" } };
        host.Content = home;
        PumpUntil(() => !page.IsLoaded && home.IsLoaded);

        pending.SetResult([Summary(name: "延迟返回的新目录")]);
        PumpFor(TimeSpan.FromMilliseconds(150));
        Ensure(ReferenceEquals(rows, page.ToolCards.ItemsSource), "工具页卸载后，旧请求仍重建隐藏的工具列表。");
        Ensure(ReferenceEquals(host.Content, home), "迟到的工具目录响应改变了当前页面。");
    });

    [Test]
    [NonParallel]
    public static void HiddenAndUnloadedProgressBarsReleaseAnimationClocks() => RunSta(() =>
    {
        var progress = new ProgressBar { IsIndeterminate = true, Width = 200, Height = 8 };
        var host = new ContentControl { Content = progress };
        using var window = new TestWindow(host);
        PumpUntil(() => progress.IsLoaded && progress.Template is not null);
        var transform = (TranslateTransform)progress.Template.FindName("IndeterminateTransform", progress);
        Ensure(transform.HasAnimatedProperties, "可见进度条没有开始动画。");
        for (var index = 0; index < 5; index++)
        {
            progress.Visibility = Visibility.Collapsed;
            PumpFor(TimeSpan.FromMilliseconds(20));
            Ensure(!transform.HasAnimatedProperties, "隐藏的进度条仍持有无限动画时钟。");
            progress.Visibility = Visibility.Visible;
            PumpFor(TimeSpan.FromMilliseconds(20));
            Ensure(transform.HasAnimatedProperties, "再次显示进度条时没有恢复动画。");
        }
        host.Content = null;
        PumpUntil(() => !progress.IsLoaded);
        Ensure(!transform.HasAnimatedProperties, "页面卸载后，进度条仍持有动画时钟。");
        host.Content = progress;
        PumpUntil(() => progress.IsLoaded);
        transform = (TranslateTransform)progress.Template.FindName("IndeterminateTransform", progress);
        Ensure(transform.HasAnimatedProperties, "页面重新加载后，进度动画没有恢复。");
    });

    [Test]
    [NonParallel]
    public static void LeavingDuringCarouselTransitionReleasesPreviousImageAndClocks() => RunSta(() =>
    {
        var carousel = new Carousel { IsLoading = false, AutoPlay = false, Height = 220 };
        var items = Enumerable.Range(0, 2).Select(index =>
        {
            var bitmap = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, new byte[256], 32);
            bitmap.Freeze();
            return new CarouselImageItem { Image = bitmap, Title = $"封面 {index}" };
        }).ToArray();
        carousel.SetItems(items);
        var host = new ContentControl { Content = carousel };
        using var window = new TestWindow(host);
        PumpUntil(() => carousel.IsLoaded);
        var front = (Image)carousel.FindName("ImageFront");
        var back = (Image)carousel.FindName("ImageBack");
        var navigate = typeof(Carousel).GetMethod("NavigateTo", BindingFlags.Instance | BindingFlags.NonPublic)!;
        navigate.Invoke(carousel, [1, false]);
        Ensure(front.HasAnimatedProperties && back.Source is not null, "测试未进入轮播图切换状态。");
        host.Content = null;
        PumpUntil(() => !carousel.IsLoaded);
        Ensure(!front.HasAnimatedProperties && !back.HasAnimatedProperties && back.Source is null,
            "离开主页后，轮播过渡仍持有图片或动画时钟。");
        host.Content = carousel;
        PumpUntil(() => carousel.IsLoaded);
        Ensure(ReferenceEquals(front.Source, items[1].Image) && front.Opacity == 1, "返回主页后没有恢复当前封面。");
        navigate.Invoke(carousel, [0, false]);
        PumpFor(TimeSpan.FromMilliseconds(450));
        Ensure(!front.HasAnimatedProperties && !back.HasAnimatedProperties && back.Source is null && front.Opacity == 1,
            "轮播过渡完成后没有释放动画时钟。");
    });

    private static ToolPackageSummary Summary(string name = "测试工具", string? icon = null) => new()
    {
        Id = "navigation-test", Name = name, Description = "目录刷新测试", Author = "Test",
        Category = "测试", LatestVersion = "1.0.0", Tags = ["test"], IconDataUrl = icon
    };

    private static void SetField(object instance, string name, object value) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);

    private sealed class TestApplication : IDisposable
    {
        private readonly Application application = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        public TestApplication() => application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/XFEToolBox.WpfCore;component/Resources/Style/ToolThemeResources.xaml", UriKind.Relative)
        });
        public void Dispose() => application.Shutdown();
    }

    private sealed class TestWindow : IDisposable
    {
        private readonly Window window;
        public TestWindow(object content)
        {
            window = new Window
            {
                Content = content, Width = 900, Height = 600, Left = -10_000, Top = -10_000,
                ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual
            };
            window.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/XFEToolBox.WpfCore;component/Resources/Style/ToolThemeResources.xaml", UriKind.Relative)
            });
            window.Show();
        }
        public void Dispose() { window.Close(); PumpFor(TimeSpan.FromMilliseconds(20)); }
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            PumpFor(TimeSpan.FromMilliseconds(10));
            Ensure(clock.Elapsed < TimeSpan.FromSeconds(5), "等待页面生命周期变化超时。");
        } while (!condition());
    }

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Ensure(thread.Join(TimeSpan.FromSeconds(15)), "页面导航生命周期测试超时。");
        if (failure is not null) throw new InvalidOperationException($"页面导航生命周期回归失败：{failure}", failure);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
