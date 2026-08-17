using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace XFEToolBox.WpfCore.Controls;

/// <summary>
/// 使用同一几何算法绘制并裁剪四个等半径圆角，避免原生 Border 在缩放后的上下圆角差异。
/// </summary>
public sealed class UniformRoundedBorder : Border
{
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (!TryGetUniformCornerRadius(out var radius) || !TryGetUniformBorderThickness(out var thickness))
        {
            base.OnRender(drawingContext);
            return;
        }

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.IsEmpty)
            return;

        if (Background is not null)
            drawingContext.DrawGeometry(Background, null, CreateGeometry(bounds, radius));

        if (BorderBrush is null || thickness <= 0)
            return;

        var outerGeometry = CreateGeometry(bounds, radius);
        var innerWidth = ActualWidth - thickness * 2;
        var innerHeight = ActualHeight - thickness * 2;
        if (innerWidth <= 0 || innerHeight <= 0)
        {
            drawingContext.DrawGeometry(BorderBrush, null, outerGeometry);
            return;
        }

        // A filled even-odd ring stays symmetric at the first and last pixel rows. A centered Pen stroke
        // follows WPF's half-open raster edge rules and produces visibly different top/bottom antialiasing.
        var borderGeometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        borderGeometry.Children.Add(outerGeometry);
        borderGeometry.Children.Add(CreateGeometry(
            new Rect(thickness, thickness, innerWidth, innerHeight),
            Math.Max(0, radius - thickness)));
        borderGeometry.Freeze();
        drawingContext.DrawGeometry(BorderBrush, null, borderGeometry);
    }

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
        // Clip is only needed for hosted content. Applying the clip to an outline-only instance also clips
        // its own one-pixel stroke at the bottom/right raster boundary and makes mirrored corners differ.
        if (Child is null || ActualWidth <= 0 || ActualHeight <= 0 || !TryGetUniformCornerRadius(out var radius))
        {
            Clip = null;
            return;
        }

        Clip = CreateGeometry(new Rect(0, 0, ActualWidth, ActualHeight), radius);
    }

    private bool TryGetUniformCornerRadius(out double radius)
    {
        radius = CornerRadius.TopLeft;
        return AreClose(radius, CornerRadius.TopRight) &&
               AreClose(radius, CornerRadius.BottomRight) &&
               AreClose(radius, CornerRadius.BottomLeft);
    }

    private bool TryGetUniformBorderThickness(out double thickness)
    {
        thickness = BorderThickness.Left;
        return AreClose(thickness, BorderThickness.Top) &&
               AreClose(thickness, BorderThickness.Right) &&
               AreClose(thickness, BorderThickness.Bottom);
    }

    private static Geometry CreateGeometry(Rect bounds, double requestedRadius)
    {
        var radius = Math.Min(Math.Max(0, requestedRadius), Math.Min(bounds.Width, bounds.Height) / 2);
        var geometry = new RectangleGeometry(bounds, radius, radius);
        geometry.Freeze();
        return geometry;
    }

    private static bool AreClose(double left, double right) => Math.Abs(left - right) < 0.001;
}
