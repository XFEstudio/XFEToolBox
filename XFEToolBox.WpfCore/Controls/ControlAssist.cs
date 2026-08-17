using System.Windows;
using System.Windows.Media;

namespace XFEToolBox.WpfCore.Controls;

/// <summary>
/// 可由公共样式使用的通用控件外观辅助属性。
/// </summary>
public static class ControlAssist
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius",
        typeof(CornerRadius),
        typeof(ControlAssist),
        new FrameworkPropertyMetadata(default(CornerRadius), FrameworkPropertyMetadataOptions.AffectsRender, OnCornerRadiusChanged));

    private static readonly DependencyProperty IsClipHandlerAttachedProperty = DependencyProperty.RegisterAttached(
        "IsClipHandlerAttached",
        typeof(bool),
        typeof(ControlAssist),
        new PropertyMetadata(false));

    public static CornerRadius GetCornerRadius(DependencyObject element) =>
        (CornerRadius)element.GetValue(CornerRadiusProperty);

    public static void SetCornerRadius(DependencyObject element, CornerRadius value) =>
        element.SetValue(CornerRadiusProperty, value);

    private static void OnCornerRadiusChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not FrameworkElement element)
            return;

        if (!(bool)element.GetValue(IsClipHandlerAttachedProperty))
        {
            element.SizeChanged += Element_SizeChanged;
            element.SetValue(IsClipHandlerAttachedProperty, true);
        }

        UpdateClip(element);
    }

    private static void Element_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement element)
            UpdateClip(element);
    }

    private static void UpdateClip(FrameworkElement element)
    {
        var radius = GetCornerRadius(element);
        if (radius == default || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            element.Clip = null;
            return;
        }

        var maximumRadius = Math.Min(element.ActualWidth, element.ActualHeight) / 2;
        var topLeft = Math.Min(radius.TopLeft, maximumRadius);
        var topRight = Math.Min(radius.TopRight, maximumRadius);
        var bottomRight = Math.Min(radius.BottomRight, maximumRadius);
        var bottomLeft = Math.Min(radius.BottomLeft, maximumRadius);
        var width = element.ActualWidth;
        var height = element.ActualHeight;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(topLeft, 0), true, true);
            context.LineTo(new Point(width - topRight, 0), true, false);
            context.QuadraticBezierTo(new Point(width, 0), new Point(width, topRight), true, false);
            context.LineTo(new Point(width, height - bottomRight), true, false);
            context.QuadraticBezierTo(new Point(width, height), new Point(width - bottomRight, height), true, false);
            context.LineTo(new Point(bottomLeft, height), true, false);
            context.QuadraticBezierTo(new Point(0, height), new Point(0, height - bottomLeft), true, false);
            context.LineTo(new Point(0, topLeft), true, false);
            context.QuadraticBezierTo(new Point(0, 0), new Point(topLeft, 0), true, false);
        }
        geometry.Freeze();
        element.Clip = geometry;
    }
}
