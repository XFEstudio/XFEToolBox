using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace XFEToolBox.Client.Views.Controls;

public sealed class ColorSwatch
{
    public ColorSwatch(string name, Color color)
    {
        Name = name;
        Color = color;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        Brush = brush;
    }

    public string Name { get; }

    public Color Color { get; }

    public Brush Brush { get; }

    public string HexValue => Color.A == byte.MaxValue
        ? $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}"
        : $"#{Color.A:X2}{Color.R:X2}{Color.G:X2}{Color.B:X2}";
}

/// <summary>
/// 包含常用色板、RGB 通道和十六进制输入的颜色选择器。
/// </summary>
public class ColorPicker : Control
{
    private static readonly DependencyPropertyKey SelectedBrushPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(SelectedBrush), typeof(Brush), typeof(ColorPicker), new PropertyMetadata(Brushes.Transparent));

    public static readonly DependencyProperty SelectedBrushProperty = SelectedBrushPropertyKey.DependencyProperty;

    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(Color), typeof(ColorPicker),
        new FrameworkPropertyMetadata(Color.FromRgb(152, 152, 231), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorChanged));

    public static readonly DependencyProperty IsDropDownOpenProperty = DependencyProperty.Register(
        nameof(IsDropDownOpen), typeof(bool), typeof(ColorPicker), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty HexValueProperty = DependencyProperty.Register(
        nameof(HexValue), typeof(string), typeof(ColorPicker),
        new FrameworkPropertyMetadata("#9898E7", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHexValueChanged));

    public static readonly DependencyProperty RedProperty = DependencyProperty.Register(
        nameof(Red), typeof(double), typeof(ColorPicker), new FrameworkPropertyMetadata(152d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnChannelChanged));

    public static readonly DependencyProperty GreenProperty = DependencyProperty.Register(
        nameof(Green), typeof(double), typeof(ColorPicker), new FrameworkPropertyMetadata(152d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnChannelChanged));

    public static readonly DependencyProperty BlueProperty = DependencyProperty.Register(
        nameof(Blue), typeof(double), typeof(ColorPicker), new FrameworkPropertyMetadata(231d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnChannelChanged));

    public static readonly RoutedEvent SelectedColorChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(SelectedColorChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<Color>), typeof(ColorPicker));

    private bool synchronizing;

    public ColorPicker()
    {
        Palette =
        [
            new("薰衣草", Color.FromRgb(152, 152, 231)),
            new("紫罗兰", Color.FromRgb(124, 103, 205)),
            new("天空蓝", Color.FromRgb(93, 155, 236)),
            new("湖水青", Color.FromRgb(63, 181, 170)),
            new("薄荷绿", Color.FromRgb(84, 190, 132)),
            new("暖阳黄", Color.FromRgb(232, 181, 77)),
            new("珊瑚橙", Color.FromRgb(231, 133, 88)),
            new("玫瑰红", Color.FromRgb(209, 98, 126)),
            new("雾灰", Color.FromRgb(126, 126, 146)),
            new("墨黑", Color.FromRgb(56, 56, 72)),
            new("纯白", Colors.White),
            new("透明", Colors.Transparent)
        ];
        UpdateDerivedValues(SelectedColor);
    }

    public ObservableCollection<ColorSwatch> Palette { get; }

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public Brush SelectedBrush => (Brush)GetValue(SelectedBrushProperty);

    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    public string HexValue
    {
        get => (string)GetValue(HexValueProperty);
        set => SetValue(HexValueProperty, value);
    }

    public double Red
    {
        get => (double)GetValue(RedProperty);
        set => SetValue(RedProperty, value);
    }

    public double Green
    {
        get => (double)GetValue(GreenProperty);
        set => SetValue(GreenProperty, value);
    }

    public double Blue
    {
        get => (double)GetValue(BlueProperty);
        set => SetValue(BlueProperty, value);
    }

    public event RoutedPropertyChangedEventHandler<Color> SelectedColorChanged
    {
        add => AddHandler(SelectedColorChangedEvent, value);
        remove => RemoveHandler(SelectedColorChangedEvent, value);
    }

    private static void OnSelectedColorChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ColorPicker picker || e.NewValue is not Color color)
            return;

        picker.UpdateDerivedValues(color);
        picker.RaiseEvent(new RoutedPropertyChangedEventArgs<Color>((Color)e.OldValue, color, SelectedColorChangedEvent));
    }

    private static void OnChannelChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ColorPicker picker || picker.synchronizing)
            return;

        picker.SetCurrentValue(SelectedColorProperty, Color.FromArgb(
            picker.SelectedColor.A,
            ToByte(picker.Red),
            ToByte(picker.Green),
            ToByte(picker.Blue)));
    }

    private static void OnHexValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ColorPicker picker || picker.synchronizing || e.NewValue is not string text)
            return;

        if (TryParseColor(text, out var color))
            picker.SetCurrentValue(SelectedColorProperty, color);
    }

    private void UpdateDerivedValues(Color color)
    {
        synchronizing = true;
        try
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            SetValue(SelectedBrushPropertyKey, brush);
            SetCurrentValue(RedProperty, (double)color.R);
            SetCurrentValue(GreenProperty, (double)color.G);
            SetCurrentValue(BlueProperty, (double)color.B);
            SetCurrentValue(HexValueProperty, color.A == byte.MaxValue
                ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");
        }
        finally
        {
            synchronizing = false;
        }
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value), byte.MinValue, byte.MaxValue);

    private static bool TryParseColor(string value, out Color color)
    {
        color = default;
        var hex = value.Trim().TrimStart('#');
        if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var numeric))
            return false;

        color = hex.Length == 6
            ? Color.FromRgb((byte)(numeric >> 16), (byte)(numeric >> 8), (byte)numeric)
            : Color.FromArgb((byte)(numeric >> 24), (byte)(numeric >> 16), (byte)(numeric >> 8), (byte)numeric);
        return true;
    }
}
