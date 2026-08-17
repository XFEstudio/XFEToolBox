using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace XFEToolBox.Client.Views.Controls;

/// <summary>
/// 可按需显示小时、分钟和秒钟的 24 小时时间选择器。
/// </summary>
public class TimePicker : Control
{
    public static readonly RoutedUICommand SelectNowCommand = new(
        "选择当前时间", nameof(SelectNowCommand), typeof(TimePicker));

    public static readonly RoutedUICommand ClearTimeCommand = new(
        "清除时间", nameof(ClearTimeCommand), typeof(TimePicker));

    public static readonly RoutedUICommand ConfirmTimeCommand = new(
        "确认时间", nameof(ConfirmTimeCommand), typeof(TimePicker));

    private static readonly DependencyPropertyKey DisplayTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayText), typeof(string), typeof(TimePicker), new PropertyMetadata("选择时间"));

    public static readonly DependencyProperty DisplayTextProperty = DisplayTextPropertyKey.DependencyProperty;

    public static readonly DependencyProperty SelectedTimeProperty = DependencyProperty.Register(
        nameof(SelectedTime), typeof(TimeSpan?), typeof(TimePicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedTimeChanged));

    public static readonly DependencyProperty HourProperty = DependencyProperty.Register(
        nameof(Hour), typeof(int), typeof(TimePicker),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPartChanged));

    public static readonly DependencyProperty MinuteProperty = DependencyProperty.Register(
        nameof(Minute), typeof(int), typeof(TimePicker),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPartChanged));

    public static readonly DependencyProperty SecondProperty = DependencyProperty.Register(
        nameof(Second), typeof(int), typeof(TimePicker),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPartChanged));

    public static readonly DependencyProperty MinuteIncrementProperty = DependencyProperty.Register(
        nameof(MinuteIncrement), typeof(int), typeof(TimePicker), new PropertyMetadata(5, OnMinuteIncrementChanged));

    public static readonly DependencyProperty SecondIncrementProperty = DependencyProperty.Register(
        nameof(SecondIncrement), typeof(int), typeof(TimePicker), new PropertyMetadata(1, OnSecondIncrementChanged));

    public static readonly DependencyProperty ShowHourProperty = DependencyProperty.Register(
        nameof(ShowHour), typeof(bool), typeof(TimePicker), new PropertyMetadata(true, OnDisplayedPartsChanged));

    public static readonly DependencyProperty ShowMinuteProperty = DependencyProperty.Register(
        nameof(ShowMinute), typeof(bool), typeof(TimePicker), new PropertyMetadata(true, OnDisplayedPartsChanged));

    public static readonly DependencyProperty ShowSecondProperty = DependencyProperty.Register(
        nameof(ShowSecond), typeof(bool), typeof(TimePicker), new PropertyMetadata(false, OnDisplayedPartsChanged));

    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(TimePicker), new PropertyMetadata("选择时间", OnPlaceholderChanged));

    public static readonly DependencyProperty IsDropDownOpenProperty = DependencyProperty.Register(
        nameof(IsDropDownOpen), typeof(bool), typeof(TimePicker),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly RoutedEvent SelectedTimeChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(SelectedTimeChanged), RoutingStrategy.Bubble,
        typeof(RoutedPropertyChangedEventHandler<TimeSpan?>), typeof(TimePicker));

    private bool synchronizing;

    public TimePicker()
    {
        Hours = Enumerable.Range(0, 24).ToArray();
        Minutes = [];
        Seconds = [];
        RebuildMinutes();
        RebuildSeconds();
        CommandBindings.Add(new CommandBinding(SelectNowCommand, (_, _) => SelectNow()));
        CommandBindings.Add(new CommandBinding(ClearTimeCommand, (_, _) => ClearTime()));
        CommandBindings.Add(new CommandBinding(ConfirmTimeCommand, (_, _) => SetCurrentValue(IsDropDownOpenProperty, false)));
    }

    public IReadOnlyList<int> Hours { get; }

    public ObservableCollection<int> Minutes { get; }

    public ObservableCollection<int> Seconds { get; }

    public TimeSpan? SelectedTime { get => (TimeSpan?)GetValue(SelectedTimeProperty); set => SetValue(SelectedTimeProperty, value); }

    public int Hour { get => (int)GetValue(HourProperty); set => SetValue(HourProperty, value); }

    public int Minute { get => (int)GetValue(MinuteProperty); set => SetValue(MinuteProperty, value); }

    public int Second { get => (int)GetValue(SecondProperty); set => SetValue(SecondProperty, value); }

    public int MinuteIncrement { get => (int)GetValue(MinuteIncrementProperty); set => SetValue(MinuteIncrementProperty, value); }

    public int SecondIncrement { get => (int)GetValue(SecondIncrementProperty); set => SetValue(SecondIncrementProperty, value); }

    public bool ShowHour { get => (bool)GetValue(ShowHourProperty); set => SetValue(ShowHourProperty, value); }

    public bool ShowMinute { get => (bool)GetValue(ShowMinuteProperty); set => SetValue(ShowMinuteProperty, value); }

    public bool ShowSecond { get => (bool)GetValue(ShowSecondProperty); set => SetValue(ShowSecondProperty, value); }

    public string PlaceholderText { get => (string)GetValue(PlaceholderTextProperty); set => SetValue(PlaceholderTextProperty, value); }

    public bool IsDropDownOpen { get => (bool)GetValue(IsDropDownOpenProperty); set => SetValue(IsDropDownOpenProperty, value); }

    public string DisplayText => (string)GetValue(DisplayTextProperty);

    public event RoutedPropertyChangedEventHandler<TimeSpan?> SelectedTimeChanged
    {
        add => AddHandler(SelectedTimeChangedEvent, value);
        remove => RemoveHandler(SelectedTimeChangedEvent, value);
    }

    private static void OnSelectedTimeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not TimePicker picker)
            return;

        picker.UpdateFromSelectedTime((TimeSpan?)e.NewValue);
        picker.RaiseEvent(new RoutedPropertyChangedEventArgs<TimeSpan?>(
            (TimeSpan?)e.OldValue, (TimeSpan?)e.NewValue, SelectedTimeChangedEvent));
    }

    private static void OnPartChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not TimePicker picker || picker.synchronizing)
            return;

        picker.SetCurrentValue(SelectedTimeProperty, new TimeSpan(
            Math.Clamp(picker.Hour, 0, 23),
            Math.Clamp(picker.Minute, 0, 59),
            Math.Clamp(picker.Second, 0, 59)));
    }

    private static void OnMinuteIncrementChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is TimePicker picker)
            picker.RebuildMinutes();
    }

    private static void OnSecondIncrementChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is TimePicker picker)
            picker.RebuildSeconds();
    }

    private static void OnDisplayedPartsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is TimePicker picker)
            picker.UpdateDisplayText(picker.SelectedTime);
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
                SetCurrentValue(SecondProperty, Math.Clamp(value.Seconds, 0, 59));
            }
        }
        finally
        {
            synchronizing = false;
        }

        UpdateDisplayText(time);
    }

    private void UpdateDisplayText(TimeSpan? time)
    {
        if (time is null || (!ShowHour && !ShowMinute && !ShowSecond))
        {
            SetValue(DisplayTextPropertyKey, PlaceholderText);
            return;
        }

        var value = time.Value;
        var parts = new List<string>(3);
        if (ShowHour)
            parts.Add($"{value.Hours:00}");
        if (ShowMinute)
            parts.Add($"{value.Minutes:00}");
        if (ShowSecond)
            parts.Add($"{value.Seconds:00}");
        SetValue(DisplayTextPropertyKey, string.Join(':', parts));
    }

    private void RebuildMinutes() => RebuildPartCollection(Minutes, MinuteIncrement, Minute, MinuteProperty);

    private void RebuildSeconds() => RebuildPartCollection(Seconds, SecondIncrement, Second, SecondProperty);

    private void RebuildPartCollection(ObservableCollection<int> collection, int requestedIncrement, int selectedValue, DependencyProperty selectedProperty)
    {
        var increment = Math.Clamp(requestedIncrement, 1, 30);
        collection.Clear();
        for (var value = 0; value < 60; value += increment)
            collection.Add(value);

        if (!collection.Contains(selectedValue))
        {
            var nearest = collection.OrderBy(value => Math.Abs(value - selectedValue)).FirstOrDefault();
            SetCurrentValue(selectedProperty, nearest);
        }
    }

    private void SelectNow()
    {
        var now = DateTime.Now;
        var minuteIncrement = Math.Clamp(MinuteIncrement, 1, 30);
        var secondIncrement = Math.Clamp(SecondIncrement, 1, 30);

        var hour = now.Hour;
        var minute = ShowMinute
            ? (int)Math.Round(now.Minute / (double)minuteIncrement) * minuteIncrement
            : now.Minute;
        var second = ShowSecond
            ? (int)Math.Round(now.Second / (double)secondIncrement) * secondIncrement
            : now.Second;

        if (second >= 60)
        {
            second = 0;
            minute++;
        }
        if (minute >= 60)
        {
            minute = 0;
            hour = (hour + 1) % 24;
        }

        SetCurrentValue(SelectedTimeProperty, new TimeSpan(hour, minute, second));
    }

    private void ClearTime()
    {
        SetCurrentValue(SelectedTimeProperty, null);
        SetCurrentValue(IsDropDownOpenProperty, false);
    }

}
