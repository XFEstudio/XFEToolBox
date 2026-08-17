using System.Windows;
using System.Windows.Media;

namespace XFEToolBox.WpfCore.Controls;

/// <summary>
/// 为客户端统一按钮模板提供主次状态和可覆盖的交互外观。
/// </summary>
public static class ButtonAssist
{
    public static readonly DependencyProperty IsPrimaryProperty = Register("IsPrimary", false);
    public static readonly DependencyProperty CornerRadiusProperty = Register("CornerRadius", new CornerRadius(11));
    public static readonly DependencyProperty HoverBackgroundProperty = Register<Brush?>("HoverBackground", null);
    public static readonly DependencyProperty HoverForegroundProperty = Register<Brush?>("HoverForeground", null);
    public static readonly DependencyProperty HoverBorderBrushProperty = Register<Brush?>("HoverBorderBrush", null);
    public static readonly DependencyProperty PressedBackgroundProperty = Register<Brush?>("PressedBackground", null);
    public static readonly DependencyProperty PressedForegroundProperty = Register<Brush?>("PressedForeground", null);
    public static readonly DependencyProperty PressedBorderBrushProperty = Register<Brush?>("PressedBorderBrush", null);
    public static readonly DependencyProperty DisabledBackgroundProperty = Register<Brush?>("DisabledBackground", null);
    public static readonly DependencyProperty DisabledForegroundProperty = Register<Brush?>("DisabledForeground", null);
    public static readonly DependencyProperty DisabledBorderBrushProperty = Register<Brush?>("DisabledBorderBrush", null);
    public static readonly DependencyProperty HoverScaleProperty = Register("HoverScale", 1.015d);
    public static readonly DependencyProperty PressedScaleProperty = Register("PressedScale", 0.975d);

    public static bool GetIsPrimary(DependencyObject element) => (bool)element.GetValue(IsPrimaryProperty);
    public static void SetIsPrimary(DependencyObject element, bool value) => element.SetValue(IsPrimaryProperty, value);

    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);

    public static Brush? GetHoverBackground(DependencyObject element) => (Brush?)element.GetValue(HoverBackgroundProperty);
    public static void SetHoverBackground(DependencyObject element, Brush? value) => element.SetValue(HoverBackgroundProperty, value);

    public static Brush? GetHoverForeground(DependencyObject element) => (Brush?)element.GetValue(HoverForegroundProperty);
    public static void SetHoverForeground(DependencyObject element, Brush? value) => element.SetValue(HoverForegroundProperty, value);

    public static Brush? GetHoverBorderBrush(DependencyObject element) => (Brush?)element.GetValue(HoverBorderBrushProperty);
    public static void SetHoverBorderBrush(DependencyObject element, Brush? value) => element.SetValue(HoverBorderBrushProperty, value);

    public static Brush? GetPressedBackground(DependencyObject element) => (Brush?)element.GetValue(PressedBackgroundProperty);
    public static void SetPressedBackground(DependencyObject element, Brush? value) => element.SetValue(PressedBackgroundProperty, value);

    public static Brush? GetPressedForeground(DependencyObject element) => (Brush?)element.GetValue(PressedForegroundProperty);
    public static void SetPressedForeground(DependencyObject element, Brush? value) => element.SetValue(PressedForegroundProperty, value);

    public static Brush? GetPressedBorderBrush(DependencyObject element) => (Brush?)element.GetValue(PressedBorderBrushProperty);
    public static void SetPressedBorderBrush(DependencyObject element, Brush? value) => element.SetValue(PressedBorderBrushProperty, value);

    public static Brush? GetDisabledBackground(DependencyObject element) => (Brush?)element.GetValue(DisabledBackgroundProperty);
    public static void SetDisabledBackground(DependencyObject element, Brush? value) => element.SetValue(DisabledBackgroundProperty, value);

    public static Brush? GetDisabledForeground(DependencyObject element) => (Brush?)element.GetValue(DisabledForegroundProperty);
    public static void SetDisabledForeground(DependencyObject element, Brush? value) => element.SetValue(DisabledForegroundProperty, value);

    public static Brush? GetDisabledBorderBrush(DependencyObject element) => (Brush?)element.GetValue(DisabledBorderBrushProperty);
    public static void SetDisabledBorderBrush(DependencyObject element, Brush? value) => element.SetValue(DisabledBorderBrushProperty, value);

    public static double GetHoverScale(DependencyObject element) => (double)element.GetValue(HoverScaleProperty);
    public static void SetHoverScale(DependencyObject element, double value) => element.SetValue(HoverScaleProperty, value);

    public static double GetPressedScale(DependencyObject element) => (double)element.GetValue(PressedScaleProperty);
    public static void SetPressedScale(DependencyObject element, double value) => element.SetValue(PressedScaleProperty, value);

    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.RegisterAttached(name, typeof(T), typeof(ButtonAssist), new FrameworkPropertyMetadata(defaultValue));
}
