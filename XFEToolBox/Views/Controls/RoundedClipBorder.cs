using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace XFEToolBox.Client.Views.Controls;

/// <summary>
/// 会按照 CornerRadius 真正裁剪子内容的 Border。
/// WPF 原生 Border 只绘制圆角，不会裁剪内部元素。
/// </summary>
public sealed class RoundedClipBorder : Border
{
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateClip();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == CornerRadiusProperty)
            UpdateClip();
    }

    private void UpdateClip()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            Clip = null;
            return;
        }

        var maximumRadius = Math.Min(ActualWidth, ActualHeight) / 2;
        var topLeft = Math.Min(CornerRadius.TopLeft, maximumRadius);
        var topRight = Math.Min(CornerRadius.TopRight, maximumRadius);
        var bottomRight = Math.Min(CornerRadius.BottomRight, maximumRadius);
        var bottomLeft = Math.Min(CornerRadius.BottomLeft, maximumRadius);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(topLeft, 0), true, true);
            context.LineTo(new Point(ActualWidth - topRight, 0), true, false);
            context.QuadraticBezierTo(new Point(ActualWidth, 0), new Point(ActualWidth, topRight), true, false);
            context.LineTo(new Point(ActualWidth, ActualHeight - bottomRight), true, false);
            context.QuadraticBezierTo(new Point(ActualWidth, ActualHeight), new Point(ActualWidth - bottomRight, ActualHeight), true, false);
            context.LineTo(new Point(bottomLeft, ActualHeight), true, false);
            context.QuadraticBezierTo(new Point(0, ActualHeight), new Point(0, ActualHeight - bottomLeft), true, false);
            context.LineTo(new Point(0, topLeft), true, false);
            context.QuadraticBezierTo(new Point(0, 0), new Point(topLeft, 0), true, false);
        }
        geometry.Freeze();
        Clip = geometry;
    }
}
