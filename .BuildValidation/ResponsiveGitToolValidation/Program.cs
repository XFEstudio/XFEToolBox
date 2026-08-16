using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XFEToolBox.Client.Views.Controls;
using XFEToolBox.Core.Tools;
using XFEToolBox.Tools.GitOperationTool;

namespace ResponsiveGitToolValidation;

internal static class Program
{
    private static readonly (int Width, int Height)[] Sizes =
    [
        (600, 356),
        (760, 520),
        (960, 656),
        (1400, 900)
    ];

    [STAThread]
    private static int Main()
    {
        const string toolId = "local.git-responsive-validation";
        ToolDataManager.ClearToolData(toolId);
        ToolDataStore.Initialize(toolId);
        try
        {
            var application = new Application();
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/XFEToolBox;component/Resources/Style/ToolThemeResources.xaml")
            });

            ValidateSharedControls();
            ValidateGitPage();
            Console.WriteLine("ResponsiveGitToolValidation: PASS - shared tabs and 4 responsive page sizes validated.");
            return 0;
        }
        finally
        {
            ToolDataManager.ClearToolData(toolId);
        }
    }

    private static void ValidateSharedControls()
    {
        var topTabs = new TopTabView { Width = 300, Height = 220 };
        for (var index = 1; index <= 8; index++)
            topTabs.Items.Add(new TabItem { Header = $"分页选项 {index}", Content = new TextBlock { Text = $"页面 {index}" } });
        Layout(topTabs, 300, 220);
        var topScroller = FindDescendant<ScrollViewer>(topTabs)
                          ?? throw new InvalidDataException("TopTabView 未生成页签滚动区域。");
        if (topScroller.ScrollableWidth <= 0)
            throw new InvalidDataException("TopTabView 在窄宽度下没有提供横向页签滚动。");

        var leftTabs = new LeftNavigationTabView { NavigationWidth = new GridLength(170) };
        leftTabs.Items.Add(new TabItem { Header = "常规", Content = new TextBlock { Text = "常规设置" } });
        leftTabs.Items.Add(new TabItem { Header = "高级", Content = new TextBlock { Text = "高级设置" } });
        leftTabs.SelectedIndex = 0;
        Layout(leftTabs, 600, 360);
        if (leftTabs.TabStripPlacement != Dock.Left || leftTabs.SelectedContent is null)
            throw new InvalidDataException("LeftNavigationTabView 未正确生成左侧导航或子页。");
    }

    private static void ValidateGitPage()
    {
        var page = new MainPage();
        try
        {
            foreach (var (width, height) in Sizes)
            {
                Layout(page, width, height);
                ValidateRepositoryHeader(page, width, height);
                ValidateResponsiveCards(page, width);
                SavePng(page, Path.Combine(AppContext.BaseDirectory, $"git-{width}x{height}.png"), width, height);
            }
        }
        finally
        {
            ((MainPageViewModel)page.DataContext).Dispose();
        }
    }

    private static void ValidateRepositoryHeader(MainPage page, double width, double height)
    {
        if (Intersects(page.RepositoryActionPanel, (FrameworkElement)page.RepositoryHeaderGrid.Children[1], page))
            throw new InvalidDataException($"{width}x{height}: 仓库路径与操作按钮发生重叠。");

        var compact = height < 520;
        if (compact && page.RepositoryDetailsGrid.Visibility != Visibility.Collapsed)
            throw new InvalidDataException($"{width}x{height}: 低高度模式没有收起仓库详情。");
        if (!compact && page.RepositoryDetailsGrid.Visibility != Visibility.Visible)
            throw new InvalidDataException($"{width}x{height}: 常规高度下仓库详情不可见。");

        if (!compact && Intersects(page.RepositoryBadgePanel, page.RepositoryStatusPanel, page))
            throw new InvalidDataException($"{width}x{height}: 仓库徽章与状态文本发生重叠。");
    }

    private static void ValidateResponsiveCards(MainPage page, double width)
    {
        var expectedRow = width < 820 ? 2 : 0;
        var elements = new FrameworkElement[]
        {
            page.SyncSecondaryCard,
            page.WorkspaceSecondaryCard,
            page.BranchSecondaryCard,
            page.AdvancedSecondaryCard
        };
        if (elements.Any(element => Grid.GetRow(element) != expectedRow))
            throw new InvalidDataException($"{width}: 双栏子页没有切换到预期的响应式排列。 ");
    }

    private static bool Intersects(FrameworkElement first, FrameworkElement second, Visual ancestor)
    {
        if (!first.IsVisible || !second.IsVisible || first.ActualWidth <= 0 || second.ActualWidth <= 0)
            return false;
        var firstBounds = first.TransformToAncestor(ancestor).TransformBounds(new Rect(0, 0, first.ActualWidth, first.ActualHeight));
        var secondBounds = second.TransformToAncestor(ancestor).TransformBounds(new Rect(0, 0, second.ActualWidth, second.ActualHeight));
        firstBounds.Intersect(secondBounds);
        return firstBounds.Width > 0.5 && firstBounds.Height > 0.5;
    }

    private static void Layout(FrameworkElement element, int width, int height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static void SavePng(FrameworkElement element, string path, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
