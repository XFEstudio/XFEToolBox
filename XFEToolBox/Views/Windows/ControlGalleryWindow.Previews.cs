using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ToolControls = XFEToolBox.Client.Views.Controls;

namespace XFEToolBox.Client.Views.Windows;

public partial class ControlGalleryWindow
{
    private FrameworkElement CreatePreview(string id) => id switch
    {
        "button" => CreateButtonPreview(),
        "text-editor" => CreateTextEditorPreview(),
        "password-editor" => CreatePasswordEditorPreview(),
        "auto-suggest" => CreateAutoSuggestPreview(),
        "check-box" => CreateCheckBoxPreview(),
        "radio-button" => CreateRadioButtonPreview(),
        "toggle-button" => CreateToggleButtonPreview(),
        "combo-box" => CreateComboBoxPreview(),
        "slider" => CreateSliderPreview(),
        "color-picker" => CreateColorPickerPreview(),
        "date-picker" => CreateDatePickerPreview(),
        "calendar-picker" => CreateCalendarPickerPreview(),
        "time-picker" => CreateTimePickerPreview(),
        "progress-bar" => CreateProgressBarPreview(),
        "progress-ring" => CreateProgressRingPreview(),
        "info-bar" => CreateInfoBarPreview(),
        "command-bar" => CreateCommandBarPreview(),
        "expander" => CreateExpanderPreview(),
        "image" => CreateImagePreview(),
        "person-picture" => CreatePersonPicturePreview(),
        "carousel" => CreateCarouselPreview(),
        "data-grid" => CreateDataGridPreview(),
        "top-tab-view" => CreateTopTabViewPreview(),
        "left-navigation-tab-view" => CreateLeftNavigationTabViewPreview(),
        "command-preview" => CreateCommandPreviewBoxPreview(),
        "scroll-text" => CreateScrollTextBlockPreview(),
        "mini-tool-button" => CreateMiniToolButtonPreview(),
        _ => new TextBlock { Text = "尚未提供此控件的演示。" }
    };

    private static FrameworkElement CreateButtonPreview()
    {
        var result = ResultText("点击任意按钮查看事件反馈");
        var normal = new Button { Content = "普通操作", Margin = new Thickness(0, 0, 9, 0) };
        var primary = new Button { Content = "主要操作", Margin = new Thickness(0, 0, 9, 0) };
        ToolControls.ButtonAssist.SetIsPrimary(primary, true);
        var disabled = new Button { Content = "不可用", IsEnabled = false };
        normal.Click += (_, _) => result.Text = "普通操作已触发";
        primary.Click += (_, _) => result.Text = "主要操作已触发";

        return PreviewStack(
            HorizontalRow(normal, primary, disabled),
            result);
    }

    private static FrameworkElement CreateTextEditorPreview()
    {
        var result = ResultText("字符数：0");
        var editor = new ToolControls.TextEditor
        {
            Width = 390,
            Height = 40,
            HintText = "输入工具名称或说明",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        editor.TextChanged += (_, _) => result.Text = $"字符数：{editor.Text.Length} · {editor.Text}";
        return PreviewStack(editor, result);
    }

    private static FrameworkElement CreatePasswordEditorPreview()
    {
        var result = ResultText("密码尚未输入");
        var editor = new ToolControls.PasswordEditor
        {
            Width = 390,
            Height = 40,
            HintText = "输入访问密钥",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        editor.PasswordChanged += (_, args) =>
            result.Text = args is null ? "密码已清空" : $"已输入 {args.Password.Length} 个字符";
        return PreviewStack(editor, result);
    }

    private static FrameworkElement CreateAutoSuggestPreview()
    {
        var result = ResultText("输入字母或中文以筛选建议");
        var suggest = new ToolControls.AutoSuggestBox
        {
            Width = 390,
            Height = 40,
            PlaceholderText = "搜索开发语言",
            MinimumPrefixLength = 1,
            ItemsSource = new[] { "C#", "C++", "TypeScript", "Python", "PowerShell", "Rust", "Kotlin", "中文脚本" },
            HorizontalAlignment = HorizontalAlignment.Left
        };
        suggest.SelectionChanged += (_, _) =>
        {
            if (suggest.SelectedItem is not null)
                result.Text = $"已选择：{suggest.SelectedItem}";
        };
        return PreviewStack(suggest, result);
    }

    private static FrameworkElement CreateCheckBoxPreview()
    {
        var result = ResultText("自动保存：开 · 仅当前项目：关");
        var first = new CheckBox { Content = "启用自动保存", IsChecked = true };
        var second = new CheckBox { Content = "仅同步当前项目", Margin = new Thickness(0, 9, 0, 0) };
        void Update() => result.Text = $"自动保存：{OnOff(first.IsChecked)} · 仅当前项目：{OnOff(second.IsChecked)}";
        first.Click += (_, _) => Update();
        second.Click += (_, _) => Update();
        return PreviewStack(first, second, result);
    }

    private static FrameworkElement CreateRadioButtonPreview()
    {
        var result = ResultText("当前通道：稳定通道");
        var stable = new RadioButton { Content = "稳定通道", GroupName = "GalleryChannel", IsChecked = true };
        var preview = new RadioButton { Content = "预览通道", GroupName = "GalleryChannel", Margin = new Thickness(0, 9, 0, 0) };
        stable.Checked += (_, _) => result.Text = "当前通道：稳定通道";
        preview.Checked += (_, _) => result.Text = "当前通道：预览通道";
        return PreviewStack(stable, preview, result);
    }

    private static FrameworkElement CreateToggleButtonPreview()
    {
        var result = ResultText("实时预览：关闭");
        var toggle = new ToggleButton { Content = "实时预览", HorizontalAlignment = HorizontalAlignment.Left };
        toggle.Click += (_, _) => result.Text = $"实时预览：{OnOff(toggle.IsChecked)}";
        return PreviewStack(toggle, result);
    }

    private static FrameworkElement CreateComboBoxPreview()
    {
        var result = ResultText("当前配置：Debug");
        var combo = new ComboBox
        {
            Width = 280,
            Height = 38,
            ItemsSource = new[] { "Debug", "Release", "Release / Self-contained" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        combo.SelectionChanged += (_, _) => result.Text = $"当前配置：{combo.SelectedItem}";
        return PreviewStack(combo, result);
    }

    private static FrameworkElement CreateSliderPreview()
    {
        var value = new TextBlock
        {
            Text = "65%",
            Foreground = AccentBrush(),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var slider = new Slider
        {
            Width = 380,
            Minimum = 0,
            Maximum = 100,
            Value = 65,
            TickFrequency = 5,
            IsSnapToTickEnabled = true
        };
        slider.ValueChanged += (_, _) => value.Text = $"{slider.Value:0}%";
        return PreviewStack(HorizontalRow(slider, value), ResultText("拖动滑块或使用方向键微调"));
    }

    private static FrameworkElement CreateColorPickerPreview()
    {
        var swatch = new Border
        {
            Width = 54,
            Height = 38,
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(12, 0, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 222, 235)),
            BorderThickness = new Thickness(1)
        };
        var result = ResultText("当前颜色：#9898E7");
        var picker = new ToolControls.ColorPicker { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        swatch.SetBinding(Border.BackgroundProperty, new Binding(nameof(ToolControls.ColorPicker.SelectedBrush)) { Source = picker });
        picker.SelectedColorChanged += (_, args) => result.Text = $"当前颜色：{ToHex(args.NewValue)}";
        return PreviewStack(HorizontalRow(picker, swatch), result);
    }

    private static FrameworkElement CreateDatePickerPreview()
    {
        var result = ResultText($"当前日期：{DateTime.Today:yyyy-MM-dd}");
        var picker = new DatePicker
        {
            Width = 260,
            SelectedDate = DateTime.Today,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        picker.SelectedDateChanged += (_, _) => result.Text = picker.SelectedDate is { } date
            ? $"当前日期：{date:yyyy-MM-dd}"
            : "尚未选择日期";
        return PreviewStack(picker, result);
    }

    private static FrameworkElement CreateCalendarPickerPreview()
    {
        var result = ResultText("点击日历按钮选择计划日期");
        var picker = new ToolControls.CalendarPicker
        {
            Width = 300,
            DisplayFormat = "yyyy年MM月dd日",
            PlaceholderText = "选择计划日期",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        picker.SelectedDateChanged += (_, _) => result.Text = picker.SelectedDate is { } date
            ? $"计划日期：{date:yyyy年MM月dd日}"
            : "尚未选择计划日期";
        return PreviewStack(picker, result);
    }

    private static FrameworkElement CreateTimePickerPreview()
    {
        var result = ResultText("当前显示：小时、分钟、秒钟");
        var picker = new ToolControls.TimePicker
        {
            Width = 300,
            MinuteIncrement = 5,
            SecondIncrement = 1,
            ShowSecond = true,
            PlaceholderText = "选择执行时间",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var showHour = new CheckBox { Content = "小时", IsChecked = true };
        var showMinute = new CheckBox { Content = "分钟", IsChecked = true, Margin = new Thickness(14, 0, 0, 0) };
        var showSecond = new CheckBox { Content = "秒钟", IsChecked = true, Margin = new Thickness(14, 0, 0, 0) };
        void UpdateParts()
        {
            picker.ShowHour = showHour.IsChecked == true;
            picker.ShowMinute = showMinute.IsChecked == true;
            picker.ShowSecond = showSecond.IsChecked == true;
            var parts = new[]
            {
                picker.ShowHour ? "小时" : null,
                picker.ShowMinute ? "分钟" : null,
                picker.ShowSecond ? "秒钟" : null
            }.Where(part => part is not null);
            result.Text = $"当前显示：{string.Join('、', parts)}";
        }
        showHour.Click += (_, _) => UpdateParts();
        showMinute.Click += (_, _) => UpdateParts();
        showSecond.Click += (_, _) => UpdateParts();
        picker.SelectedTimeChanged += (_, args) => result.Text = args.NewValue is { } time
            ? $"执行时间：{time.Hours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : "执行时间已清除";
        return PreviewStack(picker, HorizontalRow(showHour, showMinute, showSecond), result);
    }

    private static FrameworkElement CreateProgressBarPreview()
    {
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 68, Width = 380 };
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, Margin = new Thickness(0, 0, 0, 18) };
        progress.SetBinding(RangeBase.ValueProperty, new Binding(nameof(Slider.Value)) { Source = slider });
        var result = ResultText("进度：68%");
        var loop = new ToggleButton { Content = "循环动画", Margin = new Thickness(14, 0, 0, 0) };
        slider.ValueChanged += (_, _) =>
        {
            if (!progress.IsIndeterminate)
                result.Text = $"进度：{slider.Value:0}%";
        };
        loop.Click += (_, _) =>
        {
            progress.IsIndeterminate = loop.IsChecked == true;
            slider.IsEnabled = !progress.IsIndeterminate;
            result.Text = progress.IsIndeterminate ? "循环动画：运行中" : $"进度：{slider.Value:0}%";
        };
        return PreviewStack(progress, HorizontalRow(slider, loop), result);
    }

    private static FrameworkElement CreateProgressRingPreview()
    {
        var ring = new ToolControls.ProgressRing
        {
            Width = 52,
            Height = 52,
            Minimum = 0,
            Maximum = 100,
            Value = 68,
            RingThickness = 4,
            IsActive = true,
            IsIndeterminate = false
        };
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 68, Width = 300, Margin = new Thickness(18, 0, 0, 0) };
        var loop = new ToggleButton { Content = "循环动画", Margin = new Thickness(12, 0, 0, 0) };
        var active = new ToggleButton { Content = "活动", IsChecked = true, Margin = new Thickness(8, 0, 0, 0) };
        var result = ResultText("确定进度：68%");
        ring.SetBinding(RangeBase.ValueProperty, new Binding(nameof(Slider.Value)) { Source = slider });
        slider.ValueChanged += (_, _) =>
        {
            if (!ring.IsIndeterminate)
                result.Text = $"确定进度：{slider.Value:0}%";
        };
        loop.Click += (_, _) =>
        {
            ring.IsIndeterminate = loop.IsChecked == true;
            slider.IsEnabled = !ring.IsIndeterminate;
            result.Text = ring.IsIndeterminate ? "循环动画：运行中" : $"确定进度：{slider.Value:0}%";
        };
        active.Click += (_, _) => ring.IsActive = active.IsChecked == true;
        return PreviewStack(HorizontalRow(ring, slider), HorizontalRow(loop, active), result);
    }

    private static FrameworkElement CreateInfoBarPreview()
    {
        var panel = new StackPanel();
        panel.Children.Add(new ToolControls.InfoBar
        {
            Title = "工具已准备就绪",
            Message = "现在可以运行或导出工具包。",
            Severity = ToolControls.InfoBarSeverity.Informational,
            IsClosable = false
        });
        panel.Children.Add(new ToolControls.InfoBar
        {
            Title = "构建成功",
            Message = "共生成 9 个文件，没有警告。",
            Severity = ToolControls.InfoBarSeverity.Success,
            Margin = new Thickness(0, 8, 0, 0)
        });
        panel.Children.Add(new ToolControls.InfoBar
        {
            Title = "需要检查",
            Message = "发布前请更新 manifest.json 中的版本号。",
            Severity = ToolControls.InfoBarSeverity.Warning,
            Margin = new Thickness(0, 8, 0, 0)
        });
        return PreviewFrame(panel);
    }

    private static FrameworkElement CreateCommandBarPreview()
    {
        var status = ResultText("选择命令查看反馈");
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
        return PreviewStack(commandBar, status);
    }

    private static FrameworkElement CreateExpanderPreview()
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "并发请求数",
            Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 99)),
            FontWeight = FontWeights.SemiBold
        });
        content.Children.Add(new Slider { Minimum = 1, Maximum = 32, Value = 8, Width = 330, Margin = new Thickness(0, 10, 0, 0) });
        return PreviewFrame(new Expander { Header = "高级网络设置", IsExpanded = true, Content = content });
    }

    private static FrameworkElement CreateImagePreview()
    {
        var image = new Image
        {
            Width = 170,
            Height = 110,
            Source = ResourceImage("Resources/Image/default_tool_icon.png"),
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        if (Application.Current.TryFindResource("UnifiedRoundedImageStyle") is Style style)
            image.Style = style;
        return PreviewStack(image, ResultText("已应用 UnifiedRoundedImageStyle 与高质量缩放"));
    }

    private static FrameworkElement CreatePersonPicturePreview()
    {
        var online = new ToolControls.PersonPicture
        {
            Width = 62,
            Height = 62,
            DisplayName = "XFE 工作室室长",
            IsOnline = true
        };
        var initials = new ToolControls.PersonPicture
        {
            Width = 48,
            Height = 48,
            DisplayName = "Control Developer",
            Margin = new Thickness(18, 0, 0, 0)
        };
        return PreviewStack(HorizontalRow(online, initials), ResultText("未提供图片时会自动生成姓名首字"));
    }

    private static FrameworkElement CreateCarouselPreview()
    {
        var images = new ObservableCollection<ToolControls.CarouselImageItem>
        {
            new() { Image = ResourceImage("Resources/Image/toolbox.png"), Badge = "工具开发", Title = "统一控件与主题资源" },
            new() { Image = ResourceImage("Resources/Image/console.png"), Badge = "实时演示", Title = "在画廊中验证交互状态" },
            new() { Image = ResourceImage("Resources/Image/wrench.png"), Badge = "开发参考", Title = "复制 XAML 并快速开始" }
        };
        return PreviewFrame(new ToolControls.Carousel
        {
            Height = 300,
            ImageList = images,
            AutoPlay = true,
            Interval = TimeSpan.FromSeconds(4)
        });
    }

    private static FrameworkElement CreateDataGridPreview()
    {
        var grid = new DataGrid
        {
            Height = 225,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            ItemsSource = new[]
            {
                new PreviewRow("Base64 密钥生成器", "已就绪", "1.2.0"),
                new PreviewRow("局域网文件传输", "运行中", "1.1.0"),
                new PreviewRow("代码行统计", "已完成", "1.1.0")
            }
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "工具", Binding = new Binding(nameof(PreviewRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "状态", Binding = new Binding(nameof(PreviewRow.Status)), Width = 120 });
        grid.Columns.Add(new DataGridTextColumn { Header = "版本", Binding = new Binding(nameof(PreviewRow.Version)), Width = 100 });
        return PreviewFrame(grid);
    }

    private static FrameworkElement CreateTopTabViewPreview()
    {
        var tabs = new ToolControls.TopTabView { Height = 205 };
        tabs.Items.Add(Tab("概览", "显示任务概况和主要操作。"));
        tabs.Items.Add(Tab("日志", "这里展示工具运行日志。"));
        tabs.Items.Add(Tab("设置", "这里放置工具专属设置。"));
        tabs.SelectedIndex = 0;
        return PreviewFrame(tabs);
    }

    private static FrameworkElement CreateLeftNavigationTabViewPreview()
    {
        var tabs = new ToolControls.LeftNavigationTabView
        {
            Height = 230,
            NavigationWidth = new GridLength(165)
        };
        tabs.Items.Add(Tab("常规", "工具名称、默认行为与启动选项。"));
        tabs.Items.Add(Tab("网络", "连接地址、端口和超时设置。"));
        tabs.Items.Add(Tab("高级", "日志级别、缓存与诊断选项。"));
        tabs.SelectedIndex = 0;
        return PreviewFrame(tabs);
    }

    private static FrameworkElement CreateCommandPreviewBoxPreview() => PreviewFrame(new ToolControls.CommandPreviewBox
    {
        Label = "等价命令",
        CommandText = "dotnet build -c Release",
        CopyButtonText = "复制命令"
    });

    private static FrameworkElement CreateScrollTextBlockPreview()
    {
        var control = new ToolControls.ScrollTextBlock
        {
            Width = 260,
            Height = 32,
            InnerText = "C:\\XFEToolBox\\EditorWorkspaces\\LanFileTransfer\\Code\\ViewModels",
            InnerForeground = new SolidColorBrush(Color.FromRgb(75, 75, 96)),
            InnerFontSize = 11,
            AutoRolling = true,
            RollingBack = true,
            RollingTimeMillisecond = 4200,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        return PreviewStack(control, ResultText("长路径会自动滚动，移开后可回到起点"));
    }

    private static FrameworkElement CreateMiniToolButtonPreview()
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
        return PreviewStack(tool, ResultText("悬停图标可观察名称滚动，进度为 64%"));
    }

    private static Grid PreviewFrame(UIElement content)
    {
        var grid = new Grid { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.Children.Add(content);
        return grid;
    }

    private static StackPanel PreviewStack(params UIElement[] children)
    {
        var panel = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var child in children)
            panel.Children.Add(child);
        return panel;
    }

    private static StackPanel HorizontalRow(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var child in children)
            panel.Children.Add(child);
        return panel;
    }

    private static TextBlock ResultText(string text) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.FromRgb(126, 126, 146)),
        FontSize = 10,
        Margin = new Thickness(0, 14, 0, 0),
        TextWrapping = TextWrapping.Wrap
    };

    private static TabItem Tab(string header, string text) => new()
    {
        Header = header,
        Content = new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(16),
            Background = new SolidColorBrush(Color.FromRgb(247, 247, 252)),
            CornerRadius = new CornerRadius(12),
            Child = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(88, 88, 108)),
                TextWrapping = TextWrapping.Wrap
            }
        }
    };

    private static Brush AccentBrush() => Application.Current.TryFindResource("MainColor") as Brush
                                           ?? new SolidColorBrush(Color.FromRgb(152, 152, 231));

    private static BitmapImage ResourceImage(string path) => new(
        new Uri($"pack://application:,,,/XFEToolBox;component/{path}", UriKind.Absolute));

    private static string OnOff(bool? value) => value == true ? "开" : value is null ? "不确定" : "关";

    private static string ToHex(Color color) => color.A == byte.MaxValue
        ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
        : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private sealed record PreviewRow(string Name, string Status, string Version);
}
