using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ToolControls = XFEToolBox.Client.Views.Controls;

namespace XFEToolBox.Client.Views.Windows;

public partial class ControlGalleryWindow
{
    private static ScenarioPreviewResult BuildColorPickerScenarios()
    {
        var picker = new ToolControls.ColorPicker { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
        var swatch = new Border
        {
            Width = 92,
            Height = 46,
            CornerRadius = new CornerRadius(11),
            Background = picker.SelectedBrush,
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 222, 235)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var brushParameter = LiveValueParameter("SelectedBrush", picker.SelectedBrush.ToString(), out var brushText);
        picker.SelectedColorChanged += (_, args) =>
        {
            swatch.Background = picker.SelectedBrush;
            brushText.Text = $"{ToHex(args.NewValue)} · {picker.SelectedBrush}";
        };
        var colors = new[]
        {
            Color.FromRgb(152, 152, 231),
            Color.FromRgb(93, 155, 236),
            Color.FromRgb(84, 190, 132),
            Color.FromRgb(231, 133, 88)
        };

        return Scenarios(Scenario(
            "主题颜色选择",
            "预设颜色、HEX 输入和只读画刷共同演示颜色值在工具页面中的完整同步链路。",
            ScenarioPreviewStack(picker, swatch),
            ChoiceParameter("SelectedColor", new[] { "薰衣草", "天空蓝", "薄荷绿", "珊瑚橙" }, 0,
                (index, _) => picker.SelectedColor = colors[index]),
            brushParameter,
            TextParameter("HexValue", picker.HexValue, value => picker.HexValue = value)));
    }

    private static ScenarioPreviewResult BuildDatePickerScenarios()
    {
        var status = ScenarioStatus($"当前日期：{DateTime.Today:yyyy-MM-dd}");
        var picker = new DatePicker
        {
            Width = 260,
            SelectedDate = DateTime.Today,
            DisplayDateStart = DateTime.Today.AddDays(-14),
            DisplayDateEnd = DateTime.Today.AddDays(60),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        picker.SelectedDateChanged += (_, _) => status.Text = picker.SelectedDate is { } date
            ? $"当前日期：{date:yyyy-MM-dd}"
            : "尚未选择日期";

        return Scenarios(Scenario(
            "受限计划日期",
            "同时调整当前日期与可用日期边界，模拟发布计划、任务截止日期等业务约束。",
            ScenarioPreviewStack(picker, status),
            DateParameter("SelectedDate", picker.SelectedDate, value => picker.SelectedDate = value),
            DateParameter("DisplayDateStart", picker.DisplayDateStart, value =>
            {
                if (value is DateTime requestedStart && picker.DisplayDateEnd is DateTime currentEnd && requestedStart > currentEnd)
                    picker.DisplayDateEnd = requestedStart.AddDays(1);
                picker.DisplayDateStart = value;
                if (value is DateTime start && picker.SelectedDate is DateTime selected && selected < start)
                    picker.SelectedDate = start;
            }),
            DateParameter("DisplayDateEnd", picker.DisplayDateEnd, value =>
            {
                if (value is DateTime requestedEnd && picker.DisplayDateStart is DateTime currentStart && requestedEnd < currentStart)
                    picker.DisplayDateStart = requestedEnd.AddDays(-1);
                picker.DisplayDateEnd = value;
                if (value is DateTime end && picker.SelectedDate is DateTime selected && selected > end)
                    picker.SelectedDate = end;
            })));
    }

    private static ScenarioPreviewResult BuildCalendarPickerScenarios()
    {
        var status = ScenarioStatus("尚未选择计划日期");
        var picker = new ToolControls.CalendarPicker
        {
            Width = 300,
            DisplayFormat = "yyyy年MM月dd日",
            PlaceholderText = "选择计划日期",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        picker.SelectedDateChanged += (_, _) => status.Text = picker.SelectedDate is { } date
            ? $"格式化结果：{date.ToString(picker.DisplayFormat)}"
            : $"空值提示：{picker.PlaceholderText}";

        return Scenarios(Scenario(
            "格式化业务日期",
            "适用于发布日期、证书日期等需要固定展示格式和清晰空值提示的场景。",
            ScenarioPreviewStack(picker, status),
            ChoiceParameter("DisplayFormat", new[] { "yyyy年MM月dd日", "yyyy-MM-dd", "MM/dd/yyyy" }, 0,
                (_, value) =>
                {
                    picker.DisplayFormat = value;
                    if (picker.SelectedDate is { } date)
                        status.Text = $"格式化结果：{date.ToString(value)}";
                }),
            TextParameter("PlaceholderText", picker.PlaceholderText, value => picker.PlaceholderText = value),
            DateParameter("SelectedDate", null, value => picker.SelectedDate = value)));
    }

    private static ScenarioPreviewResult BuildTimePickerScenarios()
    {
        var preciseStatus = ScenarioStatus("执行时间：14:30:45");
        var precisePicker = new ToolControls.TimePicker
        {
            Width = 300,
            ShowSecond = true,
            SelectedTime = new TimeSpan(14, 30, 45),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        precisePicker.SelectedTimeChanged += (_, args) => preciseStatus.Text = args.NewValue is { } time
            ? $"执行时间：{time:hh\\:mm\\:ss}"
            : "执行时间已清除";

        var partsStatus = ScenarioStatus("显示：小时、分钟、秒钟 · 步长 5/10");
        var partsPicker = new ToolControls.TimePicker
        {
            Width = 300,
            ShowHour = true,
            ShowMinute = true,
            ShowSecond = true,
            MinuteIncrement = 5,
            SecondIncrement = 10,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        void UpdatePartsStatus()
        {
            var parts = new[]
            {
                partsPicker.ShowHour ? "小时" : null,
                partsPicker.ShowMinute ? "分钟" : null,
                partsPicker.ShowSecond ? "秒钟" : null
            }.Where(value => value is not null);
            partsStatus.Text = $"显示：{string.Join('、', parts)} · 步长 {partsPicker.MinuteIncrement}/{partsPicker.SecondIncrement}";
        }

        return Scenarios(
            Scenario(
                "精确执行时间",
                "为定时任务选择精确到秒的时间，并可由外部状态控制弹层开关。",
                ScenarioPreviewStack(precisePicker, preciseStatus),
                ChoiceParameter("SelectedTime", new[] { "08:00:00", "14:30:45", "23:59:59", "清除" }, 1,
                    (index, _) => precisePicker.SelectedTime = index switch
                    {
                        0 => new TimeSpan(8, 0, 0),
                        1 => new TimeSpan(14, 30, 45),
                        2 => new TimeSpan(23, 59, 59),
                        _ => null
                    }),
                ToggleParameter("IsDropDownOpen", false, value => precisePicker.IsDropDownOpen = value)),
            Scenario(
                "时间粒度配置",
                "按业务场景组合时、分、秒列，并分别配置分钟与秒钟的候选步长。",
                ScenarioPreviewStack(partsPicker, partsStatus),
                ToggleParameter("ShowHour", true, value => { partsPicker.ShowHour = value; UpdatePartsStatus(); }),
                ToggleParameter("ShowMinute", true, value => { partsPicker.ShowMinute = value; UpdatePartsStatus(); }),
                ToggleParameter("ShowSecond", true, value => { partsPicker.ShowSecond = value; UpdatePartsStatus(); }),
                SliderParameter("MinuteIncrement", 1, 30, 5, value => { partsPicker.MinuteIncrement = (int)value; UpdatePartsStatus(); }),
                SliderParameter("SecondIncrement", 1, 30, 10, value => { partsPicker.SecondIncrement = (int)value; UpdatePartsStatus(); })));
    }

    private static ScenarioPreviewResult BuildProgressBarScenarios()
    {
        var status = ScenarioStatus("已完成 68 / 100");
        var progress = new ProgressBar { Height = 9, Minimum = 0, Maximum = 100, Value = 68 };
        void UpdateStatus() => status.Text = $"已完成 {progress.Value:0} / {progress.Maximum:0}";

        var loadingStatus = ScenarioStatus("等待未知时长任务");
        var loading = new ProgressBar { Height = 9, IsIndeterminate = true };

        return Scenarios(
            Scenario(
                "可量化任务进度",
                "用于下载、构建等能够计算完成比例的任务，范围与当前值一起调整。",
                ScenarioPreviewStack(progress, status),
                SliderParameter("Value", 0, 100, 68, value => { progress.Value = Math.Clamp(value, progress.Minimum, progress.Maximum); UpdateStatus(); }),
                SliderParameter("Minimum", 0, 40, 0, value => { progress.Minimum = Math.Min(value, progress.Maximum - 1); UpdateStatus(); }),
                SliderParameter("Maximum", 50, 200, 100, value => { progress.Maximum = Math.Max(value, progress.Minimum + 1); UpdateStatus(); })),
            Scenario(
                "未知时长加载",
                "无法预估完成比例时使用循环动画，不应伪造一个不断增长的确定进度。",
                ScenarioPreviewStack(loading, loadingStatus),
                ToggleParameter("IsIndeterminate", true, value =>
                {
                    loading.IsIndeterminate = value;
                    loadingStatus.Text = value ? "循环动画运行中" : "循环动画已停止";
                })));
    }

    private static ScenarioPreviewResult BuildProgressRingScenarios()
    {
        var valueStatus = ScenarioStatus("确定进度：68%");
        var valueRing = new ToolControls.ProgressRing
        {
            Width = 68,
            Height = 68,
            Minimum = 0,
            Maximum = 100,
            Value = 68,
            RingThickness = 5,
            IsIndeterminate = false
        };
        void UpdateValueStatus() => valueStatus.Text = $"确定进度：{valueRing.Value:0} / {valueRing.Maximum:0}";

        var loadingStatus = ScenarioStatus("循环动画运行中");
        var loadingRing = new ToolControls.ProgressRing
        {
            Width = 68,
            Height = 68,
            RingThickness = 5,
            IsIndeterminate = true,
            IsActive = true
        };

        return Scenarios(
            Scenario(
                "环形确定进度",
                "在紧凑卡片中显示完成比例，并联合测试数值范围与笔画宽度。",
                ScenarioPreviewStack(valueRing, valueStatus),
                SliderParameter("Value", 0, 100, 68, value => { valueRing.Value = Math.Clamp(value, valueRing.Minimum, valueRing.Maximum); UpdateValueStatus(); }),
                SliderParameter("Minimum", 0, 40, 0, value => { valueRing.Minimum = Math.Min(value, valueRing.Maximum - 1); UpdateValueStatus(); }),
                SliderParameter("Maximum", 50, 200, 100, value => { valueRing.Maximum = Math.Max(value, valueRing.Minimum + 1); UpdateValueStatus(); }),
                SliderParameter("RingThickness", 2, 10, 5, value => valueRing.RingThickness = value)),
            Scenario(
                "紧凑加载状态",
                "未知时长任务使用循环弧段；IsActive 可暂停显示，宽度可适配不同尺寸。",
                ScenarioPreviewStack(loadingRing, loadingStatus),
                ToggleParameter("IsIndeterminate", true, value =>
                {
                    loadingRing.IsIndeterminate = value;
                    loadingStatus.Text = value ? "循环动画运行中" : "已切换为确定模式";
                }),
                ToggleParameter("IsActive", true, value =>
                {
                    loadingRing.IsActive = value;
                    loadingStatus.Text = value ? "环形进度已激活" : "环形进度已暂停";
                })));
    }

    private static GalleryParameter DateParameter(string propertyName, DateTime? initialValue, Action<DateTime?> changed)
    {
        var picker = new DatePicker { Height = 34, SelectedDate = initialValue };
        picker.SelectedDateChanged += (_, _) => changed(picker.SelectedDate);
        return Parameter(propertyName, picker);
    }
}
