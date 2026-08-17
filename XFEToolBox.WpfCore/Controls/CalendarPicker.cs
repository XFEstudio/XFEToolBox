using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace XFEToolBox.WpfCore.Controls;

/// <summary>
/// 支持自定义显示格式的日历日期选择器。
/// </summary>
public class CalendarPicker : DatePicker
{
    public static readonly DependencyProperty DisplayFormatProperty = DependencyProperty.Register(
        nameof(DisplayFormat), typeof(string), typeof(CalendarPicker), new PropertyMetadata("yyyy-MM-dd", OnDisplayFormatChanged));

    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(CalendarPicker), new PropertyMetadata("选择日期"));

    public string DisplayFormat
    {
        get => (string)GetValue(DisplayFormatProperty);
        set => SetValue(DisplayFormatProperty, value);
    }

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateText);
    }

    protected override void OnSelectedDateChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectedDateChanged(e);
        UpdateText();
    }

    private static void OnDisplayFormatChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is CalendarPicker picker)
            picker.UpdateText();
    }

    private void UpdateText()
    {
        if (SelectedDate is { } date)
            SetCurrentValue(TextProperty, date.ToString(DisplayFormat, CultureInfo.CurrentCulture));
    }
}
