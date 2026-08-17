using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ToolControls = XFEToolBox.WpfCore.Controls;

namespace XFEToolBox.Client.Views.Windows;

public partial class ControlGalleryWindow
{
    private static ScenarioPreviewResult BuildInfoBarScenarios()
    {
        var actionStatus = ScenarioStatus("尚未执行提示条操作");
        var actionButton = new Button { Content = "查看日志" };
        actionButton.Click += (_, _) => actionStatus.Text = "已请求打开构建日志";
        var infoBar = new ToolControls.InfoBar
        {
            Title = "工具构建完成",
            Message = "共生成 9 个文件，没有警告。",
            Severity = ToolControls.InfoBarSeverity.Success,
            IsOpen = true,
            IsClosable = true,
            ActionContent = actionButton
        };

        return Scenarios(Scenario(
            "可操作状态通知",
            "在同一条通知中切换信息等级、显示状态和右侧操作，覆盖完成、警告及错误反馈。",
            ScenarioPreviewStack(infoBar, actionStatus),
            ChoiceParameter("Severity", Enum.GetNames<ToolControls.InfoBarSeverity>(), 1,
                (_, value) => infoBar.Severity = Enum.Parse<ToolControls.InfoBarSeverity>(value)),
            ToggleParameter("IsOpen", true, value => infoBar.IsOpen = value),
            ToggleParameter("ActionContent", true, value => infoBar.ActionContent = value ? actionButton : null)));
    }

    private static ScenarioPreviewResult BuildCommandBarScenarios()
    {
        var status = ScenarioStatus("主要命令：2 · 辅助命令：1");
        var commandBar = new ToolControls.CommandBar { Header = "项目文件" };
        var create = new Button { Content = "新建" };
        ToolControls.ButtonAssist.SetIsPrimary(create, true);
        var refresh = new Button { Content = "刷新", Margin = new Thickness(8, 0, 0, 0) };
        var more = new Button { Content = "更多" };
        create.Click += (_, _) => status.Text = "已请求新建文件";
        refresh.Click += (_, _) => status.Text = "文件列表已刷新";
        more.Click += (_, _) => status.Text = "已打开更多操作";
        commandBar.Items.Add(create);
        commandBar.Items.Add(refresh);
        commandBar.SecondaryContent = more;
        var compactItems = false;

        return Scenarios(Scenario(
            "项目操作栏",
            "标题、主要命令集合与靠右辅助操作作为一个工具栏场景统一调整。",
            ScenarioPreviewStack(commandBar, status),
            TextParameter("Header", "项目文件", value => commandBar.Header = value),
            ActionParameter("Items", "切换 1 / 2 个主要命令", () =>
            {
                compactItems = !compactItems;
                commandBar.Items.Clear();
                commandBar.Items.Add(create);
                if (!compactItems)
                    commandBar.Items.Add(refresh);
                status.Text = $"主要命令：{commandBar.Items.Count}";
            }),
            ToggleParameter("SecondaryContent", true, value =>
            {
                commandBar.SecondaryContent = value ? more : null;
                status.Text = value ? "辅助命令已显示" : "辅助命令已隐藏";
            })));
    }

    private static ScenarioPreviewResult BuildExpanderScenarios()
    {
        var detailText = new TextBlock
        {
            Text = "最大并发：8 · 超时：30 秒",
            Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 99)),
            TextWrapping = TextWrapping.Wrap
        };
        var expander = new Expander
        {
            Header = "高级网络设置",
            IsExpanded = true,
            Content = new Border
            {
                Padding = new Thickness(4, 10, 4, 4),
                Child = detailText
            }
        };

        return Scenarios(Scenario(
            "渐进披露高级设置",
            "常用设置保持简洁，需要时再展开标题下的详细内容。",
            ScenarioPreviewStack(expander),
            TextParameter("Header", "高级网络设置", value => expander.Header = value),
            ToggleParameter("IsExpanded", true, value => expander.IsExpanded = value),
            TextParameter("Content", detailText.Text, value => detailText.Text = value)));
    }

    private static ScenarioPreviewResult BuildImageScenarios()
    {
        var image = new Image
        {
            Width = 230,
            Height = 145,
            Source = ResourceImage("Resources/Image/default_tool_icon.png"),
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var sources = new[]
        {
            ResourceImage("Resources/Image/default_tool_icon.png"),
            ResourceImage("Resources/Image/toolbox.png"),
            ResourceImage("Resources/Image/console.png")
        };

        var roundedImage = new Image
        {
            Width = 230,
            Height = 145,
            Source = ResourceImage("Resources/Image/toolbox.png"),
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var roundedStyle = Application.Current.TryFindResource("UnifiedRoundedImageStyle") as Style;
        roundedImage.Style = roundedStyle;

        return Scenarios(
            Scenario(
                "资源图与缩放策略",
                "切换图片来源与 Stretch，比较完整显示、填充裁剪和原始比例。",
                ScenarioPreviewStack(image),
                ChoiceParameter("Source", new[] { "默认图标", "工具箱", "控制台" }, 0,
                    (index, _) => image.Source = sources[index]),
                ChoiceParameter("Stretch", Enum.GetNames<Stretch>(), 1,
                    (_, value) => image.Stretch = Enum.Parse<Stretch>(value))),
            Scenario(
                "圆角封面图",
                "工具卡片封面使用 UnifiedRoundedImageStyle，关闭后可直接对比普通 Image。",
                ScenarioPreviewStack(roundedImage),
                ToggleParameter("Style", true, value => roundedImage.Style = value ? roundedStyle : null)));
    }

    private static ScenarioPreviewResult BuildPersonPictureScenarios()
    {
        var fallback = new ToolControls.PersonPicture
        {
            Width = 72,
            Height = 72,
            DisplayName = "XFE 工作室室长",
            IsOnline = true,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var imageProfile = new ToolControls.PersonPicture
        {
            Width = 72,
            Height = 72,
            DisplayName = "Control Developer",
            ProfilePicture = ResourceImage("Resources/Image/default_tool_icon.png"),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        return Scenarios(
            Scenario(
                "姓名首字头像",
                "没有图片时根据 DisplayName 自动生成首字，并可叠加在线状态点。",
                ScenarioPreviewStack(fallback, ScenarioStatus("主窗口左下角也使用同一头像规则")),
                TextParameter("DisplayName", fallback.DisplayName, value => fallback.DisplayName = value),
                ToggleParameter("IsOnline", true, value => fallback.IsOnline = value)),
            Scenario(
                "图片头像",
                "提供 ProfilePicture 后优先显示图片，关闭后回退到姓名首字。",
                ScenarioPreviewStack(imageProfile),
                ToggleParameter("ProfilePicture", true,
                    value => imageProfile.ProfilePicture = value ? ResourceImage("Resources/Image/default_tool_icon.png") : null)));
    }

    private static ScenarioPreviewResult BuildCarouselScenarios()
    {
        var allItems = CreateCarouselItems();
        var playbackCarousel = new ToolControls.Carousel
        {
            Height = 235,
            ImageList = new ObservableCollection<ToolControls.CarouselImageItem>(allItems),
            AutoPlay = true,
            Interval = TimeSpan.FromSeconds(4)
        };
        var playbackStatus = ScenarioStatus("自动播放：4 秒切换");

        var contentCarousel = new ToolControls.Carousel
        {
            Height = 235,
            ImageList = new ObservableCollection<ToolControls.CarouselImageItem>(allItems),
            AutoPlay = false
        };
        var showingAll = true;

        return Scenarios(
            Scenario(
                "自动推荐轮播",
                "控制自动播放与切换间隔，适用于首页推荐和教程入口。",
                ScenarioPreviewStack(playbackCarousel, playbackStatus),
                ToggleParameter("AutoPlay", true, value =>
                {
                    playbackCarousel.AutoPlay = value;
                    playbackStatus.Text = value ? $"自动播放：{playbackCarousel.Interval.TotalSeconds:0} 秒切换" : "自动播放已暂停";
                }),
                SliderParameter("Interval", 1, 10, 4, value =>
                {
                    playbackCarousel.Interval = TimeSpan.FromSeconds(value);
                    playbackStatus.Text = $"切换间隔：{value:0} 秒";
                })),
            Scenario(
                "轮播内容集合",
                "在完整推荐集和单项公告之间切换，观察导航箭头与圆点随 ImageList 自动变化。",
                ScenarioPreviewStack(contentCarousel),
                ActionParameter("ImageList", "切换单项 / 三项数据", () =>
                {
                    showingAll = !showingAll;
                    contentCarousel.ImageList = showingAll
                        ? new ObservableCollection<ToolControls.CarouselImageItem>(CreateCarouselItems())
                        : new ObservableCollection<ToolControls.CarouselImageItem>(CreateCarouselItems().Take(1));
                })));
    }

    private static ScenarioPreviewResult BuildDataGridScenarios()
    {
        var fullRows = new[]
        {
            new ScenarioPreviewRow("Base64 密钥生成器", "已就绪", "1.2.0"),
            new ScenarioPreviewRow("局域网文件传输", "运行中", "1.1.0"),
            new ScenarioPreviewRow("代码行统计", "已完成", "1.1.0")
        };
        var grid = new DataGrid
        {
            Height = 220,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            ItemsSource = fullRows
        };
        AddDataGridColumns(grid, true);
        var status = ScenarioStatus("3 行 · 3 列 · 只读");
        var fullData = true;
        var fullColumns = true;

        return Scenarios(Scenario(
            "工具任务表格",
            "切换数据集合、列配置和编辑模式，模拟日志、任务列表及批量结果。",
            ScenarioPreviewStack(grid, status),
            ActionParameter("ItemsSource", "切换完整 / 单行数据", () =>
            {
                fullData = !fullData;
                grid.ItemsSource = fullData ? fullRows : fullRows.Take(1).ToArray();
                status.Text = $"{(fullData ? 3 : 1)} 行 · {grid.Columns.Count} 列";
            }),
            ActionParameter("Columns", "切换 2 / 3 列", () =>
            {
                fullColumns = !fullColumns;
                AddDataGridColumns(grid, fullColumns);
                status.Text = $"{(fullData ? 3 : 1)} 行 · {grid.Columns.Count} 列";
            }),
            ToggleParameter("IsReadOnly", true, value =>
            {
                grid.IsReadOnly = value;
                status.Text = value ? "单元格只读" : "双击单元格可编辑";
            })));
    }

    private static ScenarioPreviewResult BuildTabViewScenarios()
    {
        var tabs = new ToolControls.TabView { Height = 205, SelectedIndex = 0 };
        AddTabViewItems(tabs, true);
        var selectedParameter = LiveValueParameter("SelectedItem", "概览", out var selectedText);
        tabs.SelectionChanged += (_, _) => selectedText.Text = (tabs.SelectedItem as TabItem)?.Header?.ToString() ?? "无";
        var allItems = true;

        return Scenarios(Scenario(
            "同级页面切换",
            "动态改变页面集合和选择状态，选中页签始终与当前内容面板保持连接。",
            ScenarioPreviewStack(tabs),
            ActionParameter("Items", "切换 2 / 3 个页面", () =>
            {
                allItems = !allItems;
                AddTabViewItems(tabs, allItems);
            }),
            SliderParameter("SelectedIndex", 0, 2, 0,
                value => tabs.SelectedIndex = Math.Min(tabs.Items.Count - 1, (int)value)),
            selectedParameter));
    }

    private static ScenarioPreviewResult BuildNavigationViewScenarios()
    {
        var navigation = new ToolControls.NavigationView
        {
            Height = 235,
            NavigationWidth = new GridLength(165),
            SelectedIndex = 0
        };
        AddNavigationItems(navigation, true);
        var allItems = true;

        return Scenarios(Scenario(
            "设置页左侧导航",
            "调整导航宽度、页面数量和当前子页，验证长页面下的独立导航区域。",
            ScenarioPreviewStack(navigation),
            SliderParameter("NavigationWidth", 120, 240, 165,
                value => navigation.NavigationWidth = new GridLength(value)),
            ActionParameter("Items", "切换 2 / 3 个页面", () =>
            {
                allItems = !allItems;
                AddNavigationItems(navigation, allItems);
            }),
            SliderParameter("SelectedIndex", 0, 2, 0,
                value => navigation.SelectedIndex = Math.Min(navigation.Items.Count - 1, (int)value))));
    }

    private static ScenarioPreviewResult BuildCommandPreviewScenarios()
    {
        var preview = new ToolControls.CommandPreviewBox
        {
            Label = "等价命令",
            CommandText = "dotnet publish -c Release --output ./artifacts",
            IsSyntaxHighlightingEnabled = true,
            CopyButtonText = "复制命令"
        };

        return Scenarios(Scenario(
            "发布命令确认",
            "修改说明、原始命令、着色开关与复制按钮文字；复制结果始终保持原始命令。",
            ScenarioPreviewStack(preview),
            TextParameter("Label", preview.Label, value => preview.Label = value),
            TextParameter("CommandText", preview.CommandText, value => preview.CommandText = value),
            ToggleParameter("IsSyntaxHighlightingEnabled", true, value => preview.IsSyntaxHighlightingEnabled = value),
            TextParameter("CopyButtonText", preview.CopyButtonText, value => preview.CopyButtonText = value)));
    }

    private static ScenarioPreviewResult BuildXamlCodeViewerScenarios()
    {
        var viewer = new ToolControls.XamlCodeViewer
        {
            Height = 150,
            Text = """
                   <!-- 工具页面标题 -->
                   <Grid Margin="16">
                       <TextBlock Text="XFE 工具页面" FontWeight="SemiBold" />
                   </Grid>
                   """,
            IsSyntaxHighlightingEnabled = true
        };

        return Scenarios(Scenario(
            "只读 XAML 示例",
            "切换语法着色或替换示例文本，验证标签、属性、字符串和注释的颜色层次。",
            ScenarioPreviewStack(viewer),
            TextParameter("Text", viewer.Text, value => viewer.Text = value),
            ToggleParameter("IsSyntaxHighlightingEnabled", true, value => viewer.IsSyntaxHighlightingEnabled = value)));
    }

    private static ScenarioPreviewResult BuildScrollTextScenarios()
    {
        var status = ScenarioStatus("长路径会在溢出时滚动");
        var control = new ToolControls.ScrollTextBlock
        {
            Width = 250,
            Height = 34,
            InnerText = "C:\\XFEToolBox\\EditorWorkspaces\\LanFileTransfer\\Code\\ViewModels",
            InnerForeground = new SolidColorBrush(Color.FromRgb(75, 75, 96)),
            AutoRolling = true,
            RollingBack = true,
            RollingTimeMillisecond = 4200,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        return Scenarios(Scenario(
            "窄区域长路径",
            "通过文本长度、自动滚动开关和动画时长模拟工具名称、路径及任务标题。",
            ScenarioPreviewStack(control, status),
            TextParameter("InnerText", control.InnerText, value => control.InnerText = value),
            ToggleParameter("AutoRolling", true, value =>
            {
                control.AutoRolling = value;
                if (value) control.StartRolling(); else control.EndRolling();
                status.Text = value ? "自动滚动已启用" : "自动滚动已停止";
            }),
            SliderParameter("RollingTimeMillisecond", 1000, 10000, 4200,
                value => control.RollingTimeMillisecond = value)));
    }

    private static ScenarioPreviewResult BuildMiniToolButtonScenarios()
    {
        var tool = new ToolControls.MiniToolButton
        {
            ToolName = "局域网文件传输",
            IconSource = ResourceImage("Resources/Image/wrench_tool.png"),
            TextColor = new SolidColorBrush(Color.FromRgb(76, 76, 98)),
            ProgressVisibility = Visibility.Visible,
            ProgressValue = 64,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var icons = new[]
        {
            ResourceImage("Resources/Image/wrench_tool.png"),
            ResourceImage("Resources/Image/console.png"),
            ResourceImage("Resources/Image/toolbox.png")
        };

        return Scenarios(Scenario(
            "工具快捷入口",
            "名称、图标和任务进度一起构成主界面工具卡片中的紧凑入口。",
            ScenarioPreviewStack(tool, ScenarioStatus("悬停图标可观察名称滚动")),
            TextParameter("ToolName", tool.ToolName, value => tool.ToolName = value),
            ChoiceParameter("IconSource", new[] { "扳手", "控制台", "工具箱" }, 0,
                (index, _) => tool.IconSource = icons[index]),
            SliderParameter("ProgressValue", 0, 100, 64, value => tool.ProgressValue = value)));
    }

    private static IReadOnlyList<ToolControls.CarouselImageItem> CreateCarouselItems() =>
    [
        new() { Image = ResourceImage("Resources/Image/toolbox.png"), Badge = "工具开发", Title = "统一控件与主题资源" },
        new() { Image = ResourceImage("Resources/Image/console.png"), Badge = "实时演示", Title = "在画廊中验证交互状态" },
        new() { Image = ResourceImage("Resources/Image/wrench.png"), Badge = "开发参考", Title = "复制 XAML 并快速开始" }
    ];

    private static void AddDataGridColumns(DataGrid grid, bool includeVersion)
    {
        grid.Columns.Clear();
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "工具",
            Binding = new System.Windows.Data.Binding(nameof(ScenarioPreviewRow.Name)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "状态",
            Binding = new System.Windows.Data.Binding(nameof(ScenarioPreviewRow.Status)),
            Width = 90
        });
        if (includeVersion)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "版本",
                Binding = new System.Windows.Data.Binding(nameof(ScenarioPreviewRow.Version)),
                Width = 72
            });
        }
    }

    private static void AddTabViewItems(ToolControls.TabView tabs, bool includeThird)
    {
        tabs.Items.Clear();
        tabs.Items.Add(ConnectedTab("概览", "显示任务概况和主要操作。"));
        tabs.Items.Add(ConnectedTab("日志", "这里展示工具运行日志。"));
        if (includeThird)
            tabs.Items.Add(ConnectedTab("设置", "这里放置工具专属设置。"));
        tabs.SelectedIndex = 0;
    }

    private static void AddNavigationItems(ToolControls.NavigationView navigation, bool includeThird)
    {
        navigation.Items.Clear();
        navigation.Items.Add(Tab("常规", "工具名称、默认行为与启动选项。"));
        navigation.Items.Add(Tab("网络", "连接地址、端口和超时设置。"));
        if (includeThird)
            navigation.Items.Add(Tab("高级", "日志级别、缓存与诊断选项。"));
        navigation.SelectedIndex = 0;
    }

    private sealed class ScenarioPreviewRow(string name, string status, string version)
    {
        public string Name { get; set; } = name;
        public string Status { get; set; } = status;
        public string Version { get; set; } = version;
    }
}
