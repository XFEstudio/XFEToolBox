using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace XFEToolBox.Client.Views.Controls;

/// <summary>
/// 24 小时时间选择器，支持可配置的分钟步长。
/// </summary>
public class TimePicker : Control
{
    private static readonly DependencyPropertyKey DisplayTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayText), typeof(string), typeof(TimePicker), new PropertyMetadata("选择时间"));

    public static readonly DependencyProperty DisplayTextProperty = DisplayTextPropertyKey.DependencyProperty;

    public static readonly DependencyProperty SelectedTimeProperty = DependencyProperty.Register(
        nameof(SelectedTime), typeof(TimeSpan?), typeof(TimePicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedTimeChanged));

    public static readonly DependencyProperty HourProperty = DependencyProperty.Register(
        nameof(Hour), typeof(int), typeof(TimePicker), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPartChanged));

    public static readonly DependencyProperty MinuteProperty = DependencyProperty.Register(
        nameof(Minute), typeof(int), typeof(TimePicker), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPartChanged));

    public static readonly DependencyProperty MinuteIncrementProperty = DependencyProperty.Register(
        nameof(MinuteIncrement), typeof(int), typeof(TimePicker), new PropertyMetadata(5, OnMinuteIncrementChanged));

    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(TimePicker), new PropertyMetadata("选择时间", OnPlaceholderChanged));

    public static readonly DependencyProperty IsDropDownOpenProperty = DependencyProperty.Register(
        nameof(IsDropDownOpen), typeof(bool), typeof(TimePicker), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly RoutedEvent SelectedTimeChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(SelectedTimeChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<TimeSpan?>), typeof(TimePicker));

    private Button? nowButton;
    private Button? clearButton;
    private Button? confirmButton;
    private bool synchronizing;

    public TimePicker()
    {
        Hours = Enumerable.Range(0, 24).ToArray();
        Minutes = [];
        RebuildMinutes();
    }

    public IReadOnlyList<int> Hours { get; }

    public ObservableCollection<int> Minutes { get; }

    public TimeSpan? SelectedTime
    {
        get => (TimeSpan?)GetValue(SelectedTimeProperty);
        set => SetValue(SelectedTimeProperty, value);
    }

    public int Hour
    {
        get => (int)GetValue(HourProperty);
        set => SetValue(HourProperty, value);
    }

    public int Minute
    {
        get => (int)GetValue(MinuteProperty);
        set => SetValue(MinuteProperty, value);
    }

    public int MinuteIncrement
    {
        get => (int)GetValue(MinuteIncrementProperty);
        set => SetValue(MinuteIncrementProperty, value);
    }

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    public string DisplayText => (string)GetValue(DisplayTextProperty);

    public event RoutedPropertyChangedEventHandler<TimeSpan?> SelectedTimeChanged
    {
        add => AddHandler(SelectedTimeChangedEvent, value);
        remove => RemoveHandler(SelectedTimeChangedEvent, value);
    }

    public override void OnApplyTemplate()
    {
        DetachButtons();
        base.OnApplyTemplate();
        nowButton = GetTemplateChild("PART_NowButton") as Button;
        clearButton = GetTemplateChild("PART_ClearButton") as Button;
        confirmButton = GetTemplateChild("PART_ConfirmButton") as Button;
        if (nowButton is not null)
            nowButton.Click += NowButton_Click;
        if (clearButton is not null)
            clearButton.Click += ClearButton_Click;
        if (confirmButton is not null)
            confirmButton.Click += ConfirmButton_Click;
    }

    private static void OnSelectedTimeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not TimePicker picker)
            return;

        picker.UpdateFromSelectedTime((TimeSpan?)e.NewValue);
        picker.RaiseEvent(new RoutedPropertyChangedEventArgs<TimeSpan?>((TimeSpan?)e.OldValue, (TimeSpan?)e.NewValue, SelectedTimeChangedEvent));
    }

    private static void OnPartChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not TimePicker picker || picker.synchronizing)
            return;

        var hour = Math.Clamp(picker.Hour, 0, 23);
        var minute = Math.Clamp(picker.Minute, 0, 59);
        picker.SetCurrentValue(SelectedTimeProperty, new TimeSpan(hour, minute, 0));
    }

    private static void OnMinuteIncrementChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is TimePicker picker)
            picker.RebuildMinutes();
    }

    private static void OnPlaceholderChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is TimePicker picker && picker.SelectedTime is null)
            picker.SetValue(DisplayTextPropertyKey, picker.PlaceholderText);
    }

    private void UpdateFromSelectedTime(TimeSpan? time)
    {
        synchronizing = true;
        try
        {
            if (time is { } value)
            {
                SetCurrentValue(HourProperty, Math.Clamp(value.Hours, 0, 23));
                SetCurrentValue(MinuteProperty, Math.Clamp(value.Minutes, 0, 59));
                SetValue(DisplayTextPropertyKey, $"{value.Hours:00}:{value.Minutes:00}");
            }
            else
            {
                SetValue(DisplayTextPropertyKey, PlaceholderText);
            }
        }
        finally
        {
            synchronizing = false;
        }
    }

    private void RebuildMinutes()
    {
        var increment = Math.Clamp(MinuteIncrement, 1, 30);
        Minutes.Clear();
        for (var minute = 0; minute < 60; minute += increment)
            Minutes.Add(minute);

        if (!Minutes.Contains(Minute))
            SetCurrentValue(MinuteProperty, Minutes.OrderBy(value => Math.Abs(value - Minute)).FirstOrDefault());
    }

    private void NowButton_Click(object sender, RoutedEventArgs e)
    {
        var now = DateTime.Now;
        var increment = Math.Clamp(MinuteIncrement, 1, 30);
        var roundedTotalMinutes = (int)Math.Round(now.Minute / (double)increment) * increment;
        var roundedHour = (now.Hour + roundedTotalMinutes / 60) % 24;
        var roundedMinute = roundedTotalMinutes % 60;
        SetCurrentValue(SelectedTimeProperty, new TimeSpan(roundedHour, roundedMinute, 0));
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        SetCurrentValue(SelectedTimeProperty, null);
        SetCurrentValue(IsDropDownOpenProperty, false);
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => SetCurrentValue(IsDropDownOpenProperty, false);

    private void DetachButtons()
    {
        if (nowButton is not null)
            nowButton.Click -= NowButton_Click;
        if (clearButton is not null)
            clearButton.Click -= ClearButton_Click;
        if (confirmButton is not null)
            confirmButton.Click -= ConfirmButton_Click;
    }
}
