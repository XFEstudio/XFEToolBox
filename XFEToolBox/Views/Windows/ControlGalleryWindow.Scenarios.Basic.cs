using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ToolControls = XFEToolBox.Client.Views.Controls;

namespace XFEToolBox.Client.Views.Windows;

public partial class ControlGalleryWindow
{
    private static ScenarioPreviewResult BuildButtonScenarios()
    {
        var status = ScenarioStatus("命令尚未执行");
        var button = new Button { Content = "生成工具包", HorizontalAlignment = HorizontalAlignment.Left };
        var command = new GalleryCommand(() => status.Text = $"Command 已执行 · {DateTime.Now:HH:mm:ss}");
        button.Command = command;
        ToolControls.ButtonAssist.SetIsPrimary(button, true);

        return Scenarios(Scenario(
            "提交操作",
            "同一场景中调整按钮内容、主次层级和命令绑定，观察外观与行为如何共同变化。",
            ScenarioPreviewStack(button, status),
            TextParameter("Content", "生成工具包", value => button.Content = value),
            ToggleParameter("IsPrimary", true, value => ToolControls.ButtonAssist.SetIsPrimary(button, value)),
            ToggleParameter("Command", true, value => button.Command = value ? command : null)));
    }

    private static ScenarioPreviewResult BuildTextEditorScenarios()
    {
        var status = ScenarioStatus("等待输入");
        var editor = new ToolControls.TextEditor
        {
            Width = 330,
            Height = 40,
            HintText = "输入工具名称",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        editor.TextChanged += (_, _) => status.Text = $"当前输入：{editor.Text} · {editor.Text.Length} 字符";
        var searchIcon = Geometry.Parse("M 9,1 A 8,8 0 1 0 9,17 A 8,8 0 1 0 9,1 M 15,15 L 22,22");

        return Scenarios(Scenario(
            "搜索与命名输入",
            "提示文字、前置图标和圆角共同构成工具页面最常用的单行输入场景。",
            ScenarioPreviewStack(editor, status),
            TextParameter("HintText", editor.HintText, value => editor.HintText = value),
            ToggleParameter("Icon", false, value => editor.Icon = value ? searchIcon : null),
            SliderParameter("EditorCornerRadius", 0, 20, 10,
                value => editor.EditorCornerRadius = new CornerRadius(value))));
    }

    private static ScenarioPreviewResult BuildPasswordEditorScenarios()
    {
        var status = ScenarioStatus("密码长度：8");
        var editor = new ToolControls.PasswordEditor
        {
            Width = 330,
            Height = 40,
            HintText = "输入访问密钥",
            Password = "XFE-2026",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        editor.PasswordChanged += (_, args) => status.Text = $"密码长度：{args?.Password.Length ?? 0}";

        return Scenarios(Scenario(
            "访问密钥输入",
            "使用测试值查看密码同步、明文显隐与遮罩字符；真实密钥不应进入画廊或日志。",
            ScenarioPreviewStack(editor, status),
            TextParameter("Password", editor.Password, value => editor.Password = value),
            ToggleParameter("PasswordVisible", false, value => editor.PasswordVisible = value),
            TextParameter("PasswordMask", "●", value => editor.PasswordMask = string.IsNullOrEmpty(value) ? "●" : value[..1])));
    }

    private static ScenarioPreviewResult BuildAutoSuggestScenarios()
    {
        var status = ScenarioStatus("输入内容以筛选当前候选集合");
        var suggestionSets = new[]
        {
            new[] { "C#", "C++", "TypeScript", "Python", "PowerShell", "Rust", "Kotlin" },
            new[] { "build", "publish", "restore", "test", "clean", "pack" },
            new[] { "首页", "工具箱", "下载专区", "服务器管理" }
        };
        var suggest = new ToolControls.AutoSuggestBox
        {
            Width = 330,
            Height = 40,
            PlaceholderText = "搜索开发语言",
            MinimumPrefixLength = 1,
            ItemsSource = suggestionSets[0],
            HorizontalAlignment = HorizontalAlignment.Left
        };
        suggest.SelectionChanged += (_, _) => status.Text = suggest.SelectedItem is null
            ? "尚未选择建议"
            : $"已选择：{suggest.SelectedItem}";

        return Scenarios(Scenario(
            "候选搜索",
            "切换候选数据、空值提示和触发前缀长度，模拟语言、命令及页面搜索。",
            ScenarioPreviewStack(suggest, status),
            ChoiceParameter("ItemsSource", new[] { "开发语言", "CLI 命令", "工具页面" }, 0,
                (index, _) => suggest.ItemsSource = suggestionSets[index]),
            TextParameter("PlaceholderText", suggest.PlaceholderText, value => suggest.PlaceholderText = value),
            SliderParameter("MinimumPrefixLength", 0, 5, 1,
                value => suggest.MinimumPrefixLength = (int)value)));
    }

    private static ScenarioPreviewResult BuildCheckBoxScenarios()
    {
        var regularStatus = ScenarioStatus("自动保存：开");
        var regular = new CheckBox { Content = "启用自动保存", IsChecked = true };
        regular.Click += (_, _) => regularStatus.Text = $"自动保存：{OnOff(regular.IsChecked)}";

        var inheritedStatus = ScenarioStatus("策略状态：不确定");
        var inherited = new CheckBox
        {
            Content = "继承工作区策略",
            IsThreeState = true,
            IsChecked = null
        };
        inherited.Click += (_, _) => inheritedStatus.Text = $"策略状态：{OnOff(inherited.IsChecked)}";

        return Scenarios(
            Scenario(
                "普通布尔设置",
                "用于明确的启用或关闭设置，标签内容和当前选择状态可同时调整。",
                ScenarioPreviewStack(regular, regularStatus),
                TextParameter("Content", "启用自动保存", value => regular.Content = value),
                TriStateParameter("IsChecked", true, value => regular.IsChecked = value)),
            Scenario(
                "继承状态",
                "三态模式适合表达“继承上级设置”，而不是把不确定状态伪装成第三个普通选项。",
                ScenarioPreviewStack(inherited, inheritedStatus),
                ToggleParameter("IsThreeState", true, value =>
                {
                    inherited.IsThreeState = value;
                    if (!value && inherited.IsChecked is null)
                        inherited.IsChecked = false;
                })));
    }

    private static ScenarioPreviewResult BuildRadioButtonScenarios()
    {
        var status = ScenarioStatus("当前通道：稳定通道");
        var stable = new RadioButton { Content = "稳定通道", GroupName = "GalleryChannel", IsChecked = true };
        var preview = new RadioButton
        {
            Content = "预览通道",
            GroupName = "GalleryChannel",
            Margin = new Thickness(0, 9, 0, 0)
        };
        stable.Checked += (_, _) => status.Text = $"当前通道：{stable.Content}";
        preview.Checked += (_, _) => status.Text = $"当前通道：{preview.Content}";

        return Scenarios(Scenario(
            "发布通道互斥选择",
            "两个选项共享 GroupName；右侧可修改首项内容、分组名和当前选择。",
            ScenarioPreviewStack(stable, preview, status),
            TextParameter("Content", "稳定通道", value => stable.Content = value),
            TextParameter("GroupName", "GalleryChannel", value =>
            {
                stable.GroupName = value;
                preview.GroupName = value;
            }),
            ToggleParameter("IsChecked", true, value => stable.IsChecked = value)));
    }

    private static ScenarioPreviewResult BuildToggleButtonScenarios()
    {
        var status = ScenarioStatus("实时预览：开");
        var toggle = new ToggleButton { Content = "实时预览", IsChecked = true, HorizontalAlignment = HorizontalAlignment.Left };
        var command = new GalleryCommand(() => status.Text = $"Command 收到状态：{OnOff(toggle.IsChecked)}");
        toggle.Command = command;
        toggle.Click += (_, _) => status.Text = $"实时预览：{OnOff(toggle.IsChecked)}";

        return Scenarios(Scenario(
            "保持型工具栏操作",
            "调整保持状态、三态能力与命令绑定，区分持续模式和一次性按钮。",
            ScenarioPreviewStack(toggle, status),
            TriStateParameter("IsChecked", true, value => toggle.IsChecked = value),
            ToggleParameter("IsThreeState", false, value =>
            {
                toggle.IsThreeState = value;
                if (!value && toggle.IsChecked is null)
                    toggle.IsChecked = false;
            }),
            ToggleParameter("Command", true, value => toggle.Command = value ? command : null)));
    }

    private static ScenarioPreviewResult BuildComboBoxScenarios()
    {
        var status = ScenarioStatus("当前配置：Debug");
        var simpleItems = new object[] { "Debug", "Release", "Release / Self-contained" };
        var objectItems = new object[]
        {
            new GalleryChoice("本机调试", "debug"),
            new GalleryChoice("便携发布", "portable"),
            new GalleryChoice("独立发布", "self-contained")
        };
        var combo = new ComboBox
        {
            Width = 310,
            Height = 38,
            ItemsSource = simpleItems,
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        combo.SelectionChanged += (_, _) => status.Text = $"当前项：{combo.SelectedItem}";

        void UseSimpleItems()
        {
            combo.DisplayMemberPath = string.Empty;
            combo.ItemsSource = simpleItems;
            combo.SelectedIndex = 0;
        }

        void UseObjectItems()
        {
            combo.DisplayMemberPath = nameof(GalleryChoice.Name);
            combo.ItemsSource = objectItems;
            combo.SelectedIndex = 0;
        }

        return Scenarios(Scenario(
            "构建配置选择",
            "在字符串集合和业务对象之间切换，并实时改变选中项与显示成员。",
            ScenarioPreviewStack(combo, status),
            ChoiceParameter("ItemsSource", new[] { "字符串集合", "业务对象集合" }, 0,
                (index, _) => { if (index == 0) UseSimpleItems(); else UseObjectItems(); }),
            SliderParameter("SelectedItem", 1, 3, 1,
                value => combo.SelectedIndex = Math.Min(combo.Items.Count - 1, (int)value - 1)),
            ChoiceParameter("DisplayMemberPath", new[] { "自动", "Name" }, 0,
                (index, _) => { if (index == 0) UseSimpleItems(); else UseObjectItems(); })));
    }

    private static ScenarioPreviewResult BuildSliderScenarios()
    {
        var valueText = new TextBlock
        {
            Text = "65",
            Foreground = AccentBrush(),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var slider = new Slider
        {
            Width = 340,
            Minimum = 0,
            Maximum = 100,
            Value = 65,
            TickFrequency = 5,
            IsSnapToTickEnabled = true
        };
        slider.ValueChanged += (_, _) => valueText.Text = slider.Value.ToString("0.##");

        return Scenarios(Scenario(
            "阈值与步进",
            "在一个完整范围场景中联合调整当前值、上下界和离散步长。",
            ScenarioPreviewStack(valueText, slider, ScenarioStatus("拖动左侧滑块也会实时更新结果")),
            SliderParameter("Value", 0, 100, 65, value => slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum)),
            SliderParameter("Minimum", 0, 60, 0, value => slider.Minimum = Math.Min(value, slider.Maximum - 1)),
            SliderParameter("Maximum", 40, 200, 100, value => slider.Maximum = Math.Max(value, slider.Minimum + 1)),
            SliderParameter("TickFrequency", 1, 20, 5, value => slider.TickFrequency = value)));
    }

    private sealed record GalleryChoice(string Name, string Value)
    {
        public override string ToString() => Name;
    }
}
