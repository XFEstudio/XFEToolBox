using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using XFEToolBox.Client.Models;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.ViewModel.Pages;
using XFEToolBox.WpfCore.Controls;
using XFEToolBox.WpfCore.Windowing;

namespace XFEToolBox.Client.Wpf.Test;

public class Program
{
    [Test]
    public static void PinnedAndRecentConfigurationRecoverFromDuplicatesAndDamage()
    {
        var pinnedSource = Enumerable.Range(0, 10)
            .Select(index => new PinnedItemEntry
            {
                Kind = LauncherItemKind.Tool,
                TargetId = $" tool-{index} ",
                AddedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-index)
            })
            .Concat([
                new PinnedItemEntry { Kind = LauncherItemKind.Tool, TargetId = "TOOL-0" },
                new PinnedItemEntry { Kind = (LauncherItemKind)999, TargetId = "unknown" },
                new PinnedItemEntry { Kind = LauncherItemKind.Page, TargetId = " " }
            ])
            .ToArray();
        var pinned = PinnedItemService.ParseEntries(JsonSerializer.Serialize(pinnedSource));
        Ensure(pinned.Count == PinnedItemService.MaximumPinnedItems, "固定项没有执行容量限制或去重。");
        Ensure(pinned[0].TargetId == "tool-0", "固定项目标 ID 没有完成规范化。");
        Ensure(PinnedItemService.ParseEntries("{损坏的 JSON").Count == 0, "损坏的固定项配置没有安全恢复。");
        Ensure(PinnedItemService.ParseEntries("[null]").Count == 0, "空固定项没有被忽略。");

        var now = DateTimeOffset.UtcNow;
        var recentSource = new[]
        {
            new RecentUsageEntry { Kind = RecentUsageKind.Tool, TargetId = "tool", Name = "旧版工具", LastUsedAtUtc = now.AddMinutes(-2) },
            new RecentUsageEntry { Kind = RecentUsageKind.Software, TargetId = "software", Name = "旧版软件", LastUsedAtUtc = now.AddMinutes(-1) },
            new RecentUsageEntry { Kind = RecentUsageKind.Project, TargetId = "project", Name = "新项目", LastUsedAtUtc = now },
            new RecentUsageEntry { Kind = RecentUsageKind.Tool, TargetId = "TOOL", Name = "重复工具", LastUsedAtUtc = now.AddMinutes(-3) },
            new RecentUsageEntry { Kind = (RecentUsageKind)999, TargetId = "unknown", Name = "未知", LastUsedAtUtc = now }
        };
        var recent = RecentUsageService.ParseEntries(JsonSerializer.Serialize(recentSource));
        Ensure(recent.Select(item => item.Kind).SequenceEqual([
            RecentUsageKind.Project,
            RecentUsageKind.Software,
            RecentUsageKind.Tool
        ]), "最近使用配置没有兼容旧类型、项目类型、排序或去重。");
        Ensure(RecentUsageService.ParseEntries("not-json").Count == 0, "损坏的最近使用配置没有安全恢复。");
        Ensure(RecentUsageService.ParseEntries("[null]").Count == 0, "空最近使用条目没有被忽略。");
    }

    [Test]
    public static void LauncherRankingUsesStableMatchOrderAndPersonalizationBoosts()
    {
        var exact = CreateLauncherItem("JSON", isPinned: false, lastUsedAtUtc: null);
        var prefix = CreateLauncherItem("JSON 格式化", isPinned: false, lastUsedAtUtc: null);
        var contains = CreateLauncherItem("转换 JSON 文档", isPinned: false, lastUsedAtUtc: null);
        var keyword = CreateLauncherItem("文本转换", isPinned: false, lastUsedAtUtc: null, "JSON");

        var exactScore = LauncherService.GetMatchScore(exact, "JSON");
        var prefixScore = LauncherService.GetMatchScore(prefix, "JSON");
        var containsScore = LauncherService.GetMatchScore(contains, "JSON");
        var keywordScore = LauncherService.GetMatchScore(keyword, "JSON");

        Ensure(exactScore < prefixScore && prefixScore < containsScore && containsScore < keywordScore,
            $"启动器匹配顺序不正确：{exactScore}, {prefixScore}, {containsScore}, {keywordScore}。");

        var pinned = CreateLauncherItem("JSON 格式化", isPinned: true, lastUsedAtUtc: null);
        var recent = CreateLauncherItem("JSON 格式化", isPinned: false, lastUsedAtUtc: DateTime.UtcNow);
        Ensure(LauncherService.GetMatchScore(pinned, "JSON") < prefixScore, "固定项没有获得同级搜索加权。");
        Ensure(LauncherService.GetMatchScore(recent, "JSON") < prefixScore, "最近使用项没有获得同级搜索加权。");
    }

    [Test]
    public static void QuickAccessToolCardLoadsItsCatalogIcon()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                const string svg = "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 8 8'><rect width='8' height='8' fill='#7b72db'/></svg>";
                var item = new LauncherItem
                {
                    Kind = LauncherItemKind.Tool,
                    TargetId = "icon-test",
                    Title = "图标测试工具",
                    IconReference = "data:image/svg+xml," + Uri.EscapeDataString(svg),
                    ExecuteAsync = static () => Task.CompletedTask
                };
                var viewModel = new LauncherItemViewModel(item);
                viewModel.IconLoadingTask.GetAwaiter().GetResult();
                Ensure(viewModel.IconSource is DrawingImage,
                    $"快速访问工具卡没有使用目录图标，而是保留了 {viewModel.IconSource.GetType().Name} 默认图标。");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Ensure(thread.Join(TimeSpan.FromSeconds(10)), "快速访问工具图标加载测试超时。");
        if (failure is not null)
            throw new InvalidOperationException($"快速访问工具图标加载失败：{failure.Message}", failure);
    }

    [Test]
    public static void WorkshopProjectCardLoadsItsManifestPreviewIcon()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "XFEToolBox.WorkshopIconTest", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Assets"));
                File.WriteAllText(Path.Combine(root, "Assets", "preview.svg"),
                    "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 8 8'><circle cx='4' cy='4' r='4' fill='#796fd6'/></svg>");
                File.WriteAllText(Path.Combine(root, "manifest.json"), """
                    {
                      "packageFormatVersion": 1,
                      "id": "local.preview-test",
                      "name": "预览图标测试",
                      "version": "1.0.0",
                      "description": "test",
                      "author": "tester",
                      "icon": "Assets/preview.svg",
                      "entry": {
                        "viewXaml": "Code/Main.xaml",
                        "viewClass": "Test.Main",
                        "viewCodeBehind": "Code/Main.xaml.cs"
                      }
                    }
                    """);

                var viewModel = new ToolProjectCardViewModel(
                    new ToolProjectHistoryItem("预览图标测试", root, DateTime.UtcNow));
                viewModel.IconLoadingTask.GetAwaiter().GetResult();
                Ensure(viewModel.IconSource is DrawingImage,
                    $"工具工坊项目卡没有使用清单预览图标，而是显示 {viewModel.IconSource.GetType().Name}。");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Ensure(thread.Join(TimeSpan.FromSeconds(10)), "工具工坊项目预览图标加载测试超时。");
        if (failure is not null)
            throw new InvalidOperationException($"工具工坊项目预览图标加载失败：{failure.Message}", failure);
    }

    [Test]
    public static void LauncherHotkeyParserNormalizesAndRejectsUnsafeGestures()
    {
        Ensure(GlobalHotkeyService.TryNormalize("control + alt + space", out var normalized, out _),
            "有效的全局热键没有通过解析。");
        Ensure(normalized == "Ctrl+Alt+Space", $"全局热键没有规范化：{normalized}。");
        Ensure(!GlobalHotkeyService.TryNormalize("Space", out _, out _), "缺少修饰键的热键不应被注册。");
        Ensure(!GlobalHotkeyService.TryNormalize("Ctrl+Alt", out _, out _), "缺少主键的热键不应被注册。");
    }

    [Test]
    public static void SingleInstanceForwardsCommandsThroughItsNamedPipe()
    {
        var scope = Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceService(scope);
        using var second = new SingleInstanceService(scope);
        Ensure(first.IsFirstInstance, "第一个实例没有取得命名互斥量。");
        Ensure(!second.IsFirstInstance, "第二个实例没有被命名互斥量识别。");

        using var received = new ManualResetEventSlim();
        var message = string.Empty;
        first.StartListening(value =>
        {
            message = value;
            received.Set();
        });
        Ensure(SingleInstanceService.SendAsync("show-palette", scope).GetAwaiter().GetResult(),
            "第二实例请求没有写入命名管道。");
        Ensure(received.Wait(TimeSpan.FromSeconds(3)), "第一实例没有收到命名管道请求。");
        Ensure(message == "show-palette", $"命名管道请求内容不正确：{message}。");
    }

    [Test]
    public static void ActivityCenterTracksProgressAndCancellation()
    {
        ActivityCenterService.ClearCompleted();
        using var activity = ActivityCenterService.Start(
            "测试下载",
            XFEToolBox.Client.Models.ActivityKind.SoftwareDownload,
            canCancel: true);

        activity.Report(42, "正在下载");
        Ensure(activity.Item.State == ActivityState.Running, "活动没有保持运行状态。");
        Ensure(activity.Item.Progress == 42, $"活动进度没有同步：{activity.Item.Progress}。");
        Ensure(activity.Item.CanCancel, "安全支持取消的活动没有显示取消能力。");

        activity.Item.CancelCommand.Execute(null);
        Ensure(activity.CancellationToken.IsCancellationRequested, "取消命令没有传递到底层 CancellationToken。");
        activity.Cancel("测试已取消");
        Ensure(activity.Item.State == ActivityState.Cancelled, "活动没有进入已取消状态。");
        ActivityCenterService.ClearCompleted();
    }

    private static LauncherItem CreateLauncherItem(
        string title,
        bool isPinned,
        DateTime? lastUsedAtUtc,
        params string[] keywords)
        => new()
        {
            Kind = LauncherItemKind.Tool,
            TargetId = Guid.NewGuid().ToString("N"),
            Title = title,
            Keywords = keywords,
            IsPinned = isPinned,
            LastUsedAtUtc = lastUsedAtUtc.HasValue ? new DateTimeOffset(lastUsedAtUtc.Value) : null,
            ExecuteAsync = static () => Task.CompletedTask
        };

    [Test]
    public static void TimePickerIncrementOneKeepsTheWholeScrollTrackUsable()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var picker = new TimePicker
                {
                    Width = 300,
                    MinuteIncrement = 1,
                    SecondIncrement = 1,
                    ShowSecond = true,
                    SelectedTime = new TimeSpan(12, 24, 30),
                    IsDropDownOpen = true
                };
                var window = new Window
                {
                    Width = 420,
                    Height = 360,
                    Left = -10_000,
                    Top = -10_000,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Content = picker
                };
                window.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/XFEToolBox.WpfCore;component/Resources/Style/ToolThemeResources.xaml", UriKind.Relative)
                });
                window.Show();
                PumpDispatcher(dispatcher);
                window.UpdateLayout();

                picker.ApplyTemplate();
                var minuteList = (ListBox?)picker.Template.FindName("PART_MinuteList", picker);
                Ensure(minuteList is not null && minuteList.Items.Count == 60,
                    "步长为 1 时分钟列没有生成完整的 60 个候选值。");
                minuteList!.ApplyTemplate();
                window.UpdateLayout();

                var scrollViewer = FindVisualDescendant<ScrollViewer>(minuteList);
                var scrollBar = FindVisualDescendants<ScrollBar>(minuteList)
                    .FirstOrDefault(candidate => candidate.Orientation == Orientation.Vertical && candidate.Visibility == Visibility.Visible);
                Ensure(scrollViewer is not null && scrollBar is not null,
                    "步长为 1 时分钟列没有显示纵向滚动条。");

                scrollBar!.ApplyTemplate();
                var track = (Track?)scrollBar.Template.FindName("PART_Track", scrollBar);
                Ensure(track is not null, "滚动条模板缺少 PART_Track，ScrollBar 无法同步完整滚动范围。");
                Ensure(track!.ActualHeight > 0 && track.Thumb.ActualHeight > 0,
                    "滚动轨道或滑块没有完成布局。");

                scrollViewer!.ScrollToEnd();
                PumpDispatcher(dispatcher);
                window.UpdateLayout();
                Ensure(scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 0.5,
                    "分钟列无法滚动到最后一个候选值。");
                Ensure(Math.Abs(track.Value - track.Maximum) <= 0.5,
                    $"滑块没有到达滚动范围底部：{track.Value:N2} / {track.Maximum:N2}。");
                var thumbBottom = track.Thumb.TranslatePoint(
                    new Point(0, track.Thumb.ActualHeight), track).Y;
                Ensure(thumbBottom <= track.ActualHeight + 0.5 && thumbBottom >= track.ActualHeight - 0.5,
                    $"滑块下半部被裁切或未到达轨道底部：{thumbBottom:N2} / {track.ActualHeight:N2}。");

                scrollViewer.ScrollToTop();
                PumpDispatcher(dispatcher);
                window.UpdateLayout();
                var thumbTop = track.Thumb.TranslatePoint(new Point(0, 0), track).Y;
                Ensure(Math.Abs(track.Value - track.Minimum) <= 0.5 && Math.Abs(thumbTop) <= 0.5,
                    "滑块无法返回滚动范围顶部。");

                window.Close();
                dispatcher.InvokeShutdown();
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
        Ensure(thread.Join(TimeSpan.FromSeconds(15)), "TimePicker 步长 1 的滚动测试超时。");
        if (failure is not null)
        {
            Console.WriteLine(failure);
            throw new InvalidOperationException("TimePicker 步长为 1 时滚动范围不完整。", failure);
        }
    }

    [Test]
    public static void XamlCodeViewerRendersDistinctSyntaxTokens()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var viewer = new XamlCodeViewer
                {
                    Text = "<!-- 示例 -->\n<controls:CommandPreviewBox Label=\"等价命令\" IsSyntaxHighlightingEnabled=\"True\" />"
                };
                var paragraph = viewer.Document.Blocks.OfType<Paragraph>().Single();
                var runs = paragraph.Inlines.OfType<Run>().ToArray();
                var distinctColors = runs
                    .Select(run => (run.Foreground as SolidColorBrush)?.Color)
                    .Where(color => color.HasValue)
                    .Distinct()
                    .Count();

                Ensure(runs.Any(run => run.Text == "controls:CommandPreviewBox"), "XAML 元素名称没有被独立分词。");
                Ensure(runs.Any(run => run.Text == "Label"), "XAML 属性名称没有被独立分词。");
                Ensure(runs.Any(run => run.Text == "\"等价命令\""), "XAML 属性值没有被独立分词。");
                Ensure(distinctColors >= 5, $"XAML 语法颜色不足：仅检测到 {distinctColors} 种颜色。");
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
        Ensure(thread.Join(TimeSpan.FromSeconds(10)), "XAML 代码查看器测试超时。");
        if (failure is not null)
            throw new InvalidOperationException("XAML 代码查看器没有正确渲染语法颜色。", failure);
    }

    [Test]
    public static void TabAndNavigationOutlinesStayInsideTheirLayoutBounds()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var tabView = new TabView { Width = 420, Height = 210, SelectedIndex = 0 };
                tabView.Items.Add(new TabItem { Header = "概览", Content = new TextBlock { Text = "概览内容" } });
                tabView.Items.Add(new TabItem { Header = "日志", Content = new TextBlock { Text = "日志内容" } });
                tabView.Items.Add(new TabItem { Header = "设置", Content = new TextBlock { Text = "设置内容" } });

                var navigationView = new NavigationView
                {
                    Width = 420,
                    Height = 230,
                    NavigationWidth = new GridLength(160),
                    SelectedIndex = 0
                };
                navigationView.Items.Add(new TabItem { Header = "常规", Content = new TextBlock { Text = "常规设置" } });
                navigationView.Items.Add(new TabItem { Header = "网络", Content = new TextBlock { Text = "网络设置" } });
                navigationView.Items.Add(new TabItem { Header = "高级", Content = new TextBlock { Text = "高级设置" } });
                navigationView.Items.Add(new TabItem { Header = "外观", Content = new TextBlock { Text = "外观设置" } });
                navigationView.Items.Add(new TabItem { Header = "通知", Content = new TextBlock { Text = "通知设置" } });
                navigationView.Items.Add(new TabItem { Header = "隐私", Content = new TextBlock { Text = "隐私设置" } });

                var panel = new StackPanel();
                panel.Children.Add(tabView);
                panel.Children.Add(navigationView);
                var window = new Window
                {
                    Width = 480,
                    Height = 500,
                    Left = -10_000,
                    Top = -10_000,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Content = panel
                };
                window.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/XFEToolBox.WpfCore;component/Resources/Style/ToolThemeResources.xaml", UriKind.Relative)
                });
                window.Show();
                PumpDispatcher(dispatcher);

                for (var index = 0; index < tabView.Items.Count; index++)
                {
                    tabView.SelectedIndex = index;
                    PumpDispatcher(dispatcher);
                    window.UpdateLayout();

                    var item = (TabItem)tabView.Items[index];
                    item.ApplyTemplate();
                    var outline = (Border?)item.Template.FindName("SelectedOutline", item);
                    var headerContent = (ContentPresenter?)item.Template.FindName("HeaderContent", item);
                    var connector = (Border?)item.Template.FindName("ContentConnector", item);

                    Ensure(outline is { Visibility: Visibility.Visible, ActualWidth: >= 1 }, $"第 {index + 1} 个页签轮廓没有显示。");
                    Ensure(outline!.BorderThickness == new Thickness(1, 1, 1, 0), $"第 {index + 1} 个页签轮廓缺少侧边框。");
                    Ensure(connector is { Visibility: Visibility.Visible }, $"第 {index + 1} 个页签没有与内容面板连接。");

                    var itemRight = item.TranslatePoint(new Point(item.ActualWidth, 0), window).X;
                    var edgeRight = outline!.TranslatePoint(new Point(outline.ActualWidth, 0), window).X;
                    Ensure(edgeRight <= itemRight - 4,
                        $"第 {index + 1} 个页签右边框仍位于可裁剪边界：{edgeRight:N2} >= {itemRight:N2}。");
                    Ensure(headerContent is not null, $"第 {index + 1} 个页签缺少标题内容。");
                    var outlineLeft = outline.TranslatePoint(new Point(0, 0), window).X;
                    var headerLeft = headerContent!.TranslatePoint(new Point(0, 0), window).X;
                    var outlineCenter = outlineLeft + outline.ActualWidth / 2;
                    var headerCenter = headerLeft + headerContent.ActualWidth / 2;
                    Ensure(Math.Abs(outlineCenter - headerCenter) <= 0.5,
                        $"第 {index + 1} 个页签标题没有居中：{headerCenter:N2} != {outlineCenter:N2}。");
                }

                tabView.SelectedIndex = 0;
                PumpDispatcher(dispatcher);
                window.UpdateLayout();
                var firstItem = (TabItem)tabView.Items[0];
                var firstOutline = (Border?)firstItem.Template.FindName("SelectedOutline", firstItem);
                var contentFrame = (Border?)tabView.Template.FindName("ContentFrame", tabView);
                Ensure(firstOutline is not null && contentFrame is not null, "TabView 缺少用于对齐的轮廓或内容面板。");
                var firstOutlineLeft = firstOutline!.TranslatePoint(new Point(0, 0), window).X;
                var contentFrameLeft = contentFrame!.TranslatePoint(new Point(0, 0), window).X;
                Ensure(Math.Abs(firstOutlineLeft - contentFrameLeft) <= 0.5,
                    $"首个页签与内容面板左侧没有对齐：{firstOutlineLeft:N2} != {contentFrameLeft:N2}。");

                navigationView.ApplyTemplate();
                window.UpdateLayout();
                var navigationFrameHost = (Grid?)navigationView.Template.FindName("NavigationFrameHost", navigationView);
                var navigationClip = (UniformRoundedBorder?)navigationView.Template.FindName("NavigationClip", navigationView);
                var navigationFrame = (Border?)navigationView.Template.FindName("NavigationFrame", navigationView);
                Ensure(navigationFrameHost is not null && navigationClip is not null && navigationFrame is not null,
                    "NavigationView 缺少独立的圆角裁剪层或描边层。");
                Ensure(navigationFrame!.ActualWidth > 0 && navigationFrame.ActualHeight > 0, "NavigationView 导航边框没有完成布局。");
                Ensure(navigationFrame.BorderThickness == new Thickness(1), "NavigationView 四侧边框厚度不完整。");
                Ensure(navigationFrame.CornerRadius == new CornerRadius(15), "NavigationView 四角没有保持统一圆角。");
                Ensure(navigationFrameHost!.Margin == new Thickness(2), "NavigationView 边框没有保留防裁剪间距。");
                Ensure(navigationClip!.CornerRadius == navigationFrame.CornerRadius,
                    "NavigationView 裁剪层与描边层的圆角半径不一致。");
                Ensure(Math.Abs(navigationClip.ActualWidth - navigationFrame.ActualWidth) <= 0.5 &&
                       Math.Abs(navigationClip.ActualHeight - navigationFrame.ActualHeight) <= 0.5,
                    "NavigationView 裁剪层与描边层的尺寸不一致。");
                Ensure(navigationFrame is UniformRoundedBorder && navigationClip.Clip is RectangleGeometry,
                    "NavigationView 没有使用圆角裁剪，内部内容可能覆盖下半部圆角。");
                EnsureVerticallyMirroredCorners(navigationFrame, 18);
                for (var index = 0; index < navigationView.Items.Count; index++)
                {
                    var item = (TabItem)navigationView.Items[index];
                    item.ApplyTemplate();
                    var surface = (Border?)item.Template.FindName("Surface", item);
                    var headerContent = (ContentPresenter?)item.Template.FindName("HeaderContent", item);
                    Ensure(surface is not null && headerContent is not null, $"第 {index + 1} 个导航项缺少表面或标题内容。");
                    var surfaceLeft = surface!.TranslatePoint(new Point(0, 0), item).X;
                    var headerLeft = headerContent!.TranslatePoint(new Point(0, 0), item).X;
                    var surfaceCenter = surfaceLeft + surface.ActualWidth / 2;
                    var headerCenter = headerLeft + headerContent.ActualWidth / 2;
                    Ensure(Math.Abs(surfaceCenter - headerCenter) <= 0.5,
                        $"第 {index + 1} 个导航项标题没有居中：{headerCenter:N2} != {surfaceCenter:N2}。");
                }
                var navigationScroller = (ScrollViewer?)navigationView.Template.FindName("NavigationScroller", navigationView);
                Ensure(navigationScroller is not null, "NavigationView 缺少独立导航滚动区域。");
                navigationScroller!.ScrollToEnd();
                PumpDispatcher(dispatcher);
                window.UpdateLayout();
                Ensure(navigationScroller.VerticalOffset > 0, "NavigationView 滚动状态没有被覆盖测试。");
                Ensure(navigationClip.Clip is RectangleGeometry, "NavigationView 滚动后丢失圆角裁剪。");

                window.Close();
                dispatcher.InvokeShutdown();
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
        Ensure(thread.Join(TimeSpan.FromSeconds(15)), "页签与导航边框测试超时。");
        if (failure is not null)
        {
            Console.WriteLine(failure);
            throw new InvalidOperationException("页签或导航边框可能再次被裁剪。", failure);
        }
    }

    [Test]
    public static void FramelessMaximizedWindowStaysInsideMonitorWorkArea()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var window = new Window
                {
                    Width = 1_000,
                    Height = 700,
                    MinWidth = 640,
                    MinHeight = 480,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.CanResize,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Opacity = 0.01
                };
                WindowChrome.SetWindowChrome(window, new WindowChrome
                {
                    CaptionHeight = 0,
                    CornerRadius = new CornerRadius(18),
                    GlassFrameThickness = new Thickness(0),
                    ResizeBorderThickness = new Thickness(6),
                    UseAeroCaptionButtons = false
                });
                WindowWorkAreaHelper.Attach(window);
                window.Show();
                window.WindowState = WindowState.Maximized;

                PumpDispatcher(dispatcher);
                window.UpdateLayout();

                var handle = new WindowInteropHelper(window).Handle;
                Ensure(GetWindowRect(handle, out var windowRect), "无法读取最大化窗口边界。");
                var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
                var monitorInfo = MonitorInfo.Create();
                Ensure(monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref monitorInfo), "无法读取显示器工作区。");

                const int tolerance = 1;
                Ensure(Math.Abs(windowRect.Left - monitorInfo.WorkArea.Left) <= tolerance,
                    $"窗口左边界越界：{windowRect.Left} != {monitorInfo.WorkArea.Left}。");
                Ensure(Math.Abs(windowRect.Top - monitorInfo.WorkArea.Top) <= tolerance,
                    $"窗口上边界越界：{windowRect.Top} != {monitorInfo.WorkArea.Top}。");
                Ensure(Math.Abs(windowRect.Right - monitorInfo.WorkArea.Right) <= tolerance,
                    $"窗口右边界越界：{windowRect.Right} != {monitorInfo.WorkArea.Right}。");
                Ensure(Math.Abs(windowRect.Bottom - monitorInfo.WorkArea.Bottom) <= tolerance,
                    $"窗口下边界越界：{windowRect.Bottom} != {monitorInfo.WorkArea.Bottom}。");

                window.Close();
                dispatcher.InvokeShutdown();
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
        Ensure(thread.Join(TimeSpan.FromSeconds(15)), "无边框窗口最大化测试超时。");
        if (failure is not null)
            throw new InvalidOperationException("无边框窗口没有正确限制在显示器工作区。", failure);
    }

    [SMTest]
    public static void BufferedConsoleRendererKeepsUiTreeBoundedUnderLoad()
    {
        const int outputCount = 20_000;
        const int maxLines = 8_000;
        Exception? failure = null;
        double throughput = 0;
        double maxUiDelayMilliseconds = 0;
        var renderedLines = 0;
        var visualBlocks = 0;

        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var outputList = new ListBox();
                ScrollViewer.SetCanContentScroll(outputList, true);
                VirtualizingPanel.SetIsVirtualizing(outputList, true);
                VirtualizingPanel.SetVirtualizationMode(outputList, VirtualizationMode.Recycling);
                VirtualizingPanel.SetScrollUnit(outputList, ScrollUnit.Pixel);
                var window = new Window
                {
                    Width = 1_200,
                    Height = 700,
                    Left = -10_000,
                    Top = -10_000,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Content = outputList
                };
                var renderer = new BufferedConsoleRenderer(dispatcher, outputList, () => maxLines);
                window.Show();
                window.UpdateLayout();

                // Exclude one-time WPF/graphics initialization from the output latency metric.
                var warmupFrame = new DispatcherFrame();
                _ = dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => warmupFrame.Continue = false));
                Dispatcher.PushFrame(warmupFrame);

                var stopwatch = Stopwatch.StartNew();
                var lastHeartbeat = stopwatch.Elapsed;
                var heartbeat = new DispatcherTimer(DispatcherPriority.Input, dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(10)
                };
                heartbeat.Tick += (_, _) =>
                {
                    var now = stopwatch.Elapsed;
                    maxUiDelayMilliseconds = Math.Max(maxUiDelayMilliseconds, (now - lastHeartbeat).TotalMilliseconds);
                    lastHeartbeat = now;
                };
                heartbeat.Start();

                for (var index = 0; index < outputCount; index++)
                    renderer.Append($"line {index}", Colors.White, true);

                var frame = new DispatcherFrame();
                renderer.ContentChanged += hasContent =>
                {
                    if (hasContent && outputList.Items.Count > 0)
                        outputList.ScrollIntoView(outputList.Items[^1]);

                    if (renderer.PendingCount != 0)
                        return;

                    _ = dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                    {
                        window.UpdateLayout();
                        var now = stopwatch.Elapsed;
                        maxUiDelayMilliseconds = Math.Max(maxUiDelayMilliseconds, (now - lastHeartbeat).TotalMilliseconds);
                        heartbeat.Stop();
                        window.Close();
                        frame.Continue = false;
                    }));
                };

                Dispatcher.PushFrame(frame);
                stopwatch.Stop();

                renderedLines = renderer.RenderedLineCount;
                visualBlocks = outputList.Items.Count;
                throughput = outputCount / stopwatch.Elapsed.TotalSeconds;

                Ensure(renderedLines <= maxLines, $"渲染行数超过配置上限：{renderedLines} > {maxLines}。");
                Ensure(renderedLines >= maxLines - 127, $"分块裁剪保留的行数过少：{renderedLines}。");
                Ensure(visualBlocks <= Math.Ceiling(maxLines / 128d), $"控制台视觉树过大：{visualBlocks} 个文本块。");
                Ensure(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"WPF 控制台渲染耗时过长：{stopwatch.Elapsed}。");
                Ensure(maxUiDelayMilliseconds < 250, $"WPF UI 线程单次无响应过长：{maxUiDelayMilliseconds:N1} ms。");

                dispatcher.InvokeShutdown();
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
        Ensure(thread.Join(TimeSpan.FromSeconds(30)), "WPF 控制台渲染测试超时。");
        if (failure is not null)
        {
            Console.WriteLine(failure);
            throw new InvalidOperationException("WPF 控制台渲染测试失败。", failure);
        }

        Console.WriteLine($"WPF 控制台端到端吞吐：{throughput:N0} 条/秒；UI 最大调度延迟 {maxUiDelayMilliseconds:N1} ms；保留 {renderedLines:N0} 行，仅 {visualBlocks} 个文本块。");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void PumpDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        _ = dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static T? FindVisualDescendant<T>(DependencyObject root) where T : DependencyObject
        => FindVisualDescendants<T>(root).FirstOrDefault();

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualDescendants<T>(child))
                yield return descendant;
        }
    }

    private static void EnsureVerticallyMirroredCorners(FrameworkElement element, int sampleSize)
    {
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);
        var size = Math.Min(sampleSize, Math.Min(width, height) / 2);
        var maximumDifference = 0;
        var differenceLocation = string.Empty;
        var differenceValues = string.Empty;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var leftDifference = PixelDifference(pixels, stride, x, y, x, height - 1 - y);
                if (leftDifference > maximumDifference)
                {
                    maximumDifference = leftDifference;
                    differenceLocation = $"left ({x},{y})/({x},{height - 1 - y})";
                    differenceValues = $"{PixelValue(pixels, stride, x, y)}/{PixelValue(pixels, stride, x, height - 1 - y)}";
                }
                var rightDifference = PixelDifference(pixels, stride, width - 1 - x, y, width - 1 - x, height - 1 - y);
                if (rightDifference > maximumDifference)
                {
                    maximumDifference = rightDifference;
                    differenceLocation = $"right ({width - 1 - x},{y})/({width - 1 - x},{height - 1 - y})";
                    differenceValues = $"{PixelValue(pixels, stride, width - 1 - x, y)}/{PixelValue(pixels, stride, width - 1 - x, height - 1 - y)}";
                }
            }
        }

        Ensure(maximumDifference <= 1,
            $"NavigationView 上下圆角的像素差异过大：{maximumDifference}，位置 {differenceLocation}，像素 {differenceValues}，布局 {element.ActualWidth:N3}×{element.ActualHeight:N3}，位图 {width}×{height}。");
    }

    private static int PixelDifference(byte[] pixels, int stride, int firstX, int firstY, int secondX, int secondY)
    {
        var first = firstY * stride + firstX * 4;
        var second = secondY * stride + secondX * 4;
        var difference = 0;
        for (var channel = 0; channel < 4; channel++)
            difference = Math.Max(difference, Math.Abs(pixels[first + channel] - pixels[second + channel]));
        return difference;
    }

    private static string PixelValue(byte[] pixels, int stride, int x, int y)
    {
        var offset = y * stride + x * 4;
        return $"[{pixels[offset]},{pixels[offset + 1]},{pixels[offset + 2]},{pixels[offset + 3]}]";
    }

    private const uint MonitorDefaultToNearest = 0x00000002;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;

        public static MonitorInfo Create() => new()
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };
    }
}
