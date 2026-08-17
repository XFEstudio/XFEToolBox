using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace XFEToolBox.WpfCore.Controls;

/// <summary>
/// 同时支持确定进度和循环忙碌状态的环形进度控件。
/// </summary>
public class ProgressRing : RangeBase
{
    private static readonly DependencyProperty RotationAngleProperty = DependencyProperty.Register(
        "RotationAngle", typeof(double), typeof(ProgressRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(ProgressRing),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnAnimationStateChanged));

    public static readonly DependencyProperty IsIndeterminateProperty = DependencyProperty.Register(
        nameof(IsIndeterminate), typeof(bool), typeof(ProgressRing),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnAnimationStateChanged));

    public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
        nameof(RingThickness), typeof(double), typeof(ProgressRing),
        new FrameworkPropertyMetadata(3d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            null, CoerceRingThickness));

    public ProgressRing()
    {
        Loaded += (_, _) => UpdateAnimation();
        Unloaded += (_, _) => StopAnimation();
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    public double RingThickness
    {
        get => (double)GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var thickness = Math.Clamp(RingThickness, 0.5, Math.Max(0.5, Math.Min(ActualWidth, ActualHeight) / 2));
        var radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - thickness / 2);
        if (radius <= 0)
            return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var trackBrush = Background ?? Brushes.Transparent;
        var progressBrush = Foreground ?? Brushes.Transparent;
        var trackPen = CreatePen(trackBrush, thickness);
        var progressPen = CreatePen(progressBrush, thickness);

        drawingContext.DrawEllipse(null, trackPen, center, radius, radius);

        if (!IsActive)
            drawingContext.PushOpacity(0.34);

        if (IsIndeterminate)
        {
            DrawArc(drawingContext, progressPen, center, radius, -90 + (double)GetValue(RotationAngleProperty), 96);
        }
        else
        {
            var range = Maximum - Minimum;
            var progress = range <= 0 ? 0 : Math.Clamp((Value - Minimum) / range, 0, 1);
            if (progress >= 0.9999)
                drawingContext.DrawEllipse(null, progressPen, center, radius, radius);
            else if (progress > 0)
                DrawArc(drawingContext, progressPen, center, radius, -90, progress * 359.9);
        }

        if (!IsActive)
            drawingContext.Pop();
    }

    protected override void OnMinimumChanged(double oldMinimum, double newMinimum)
    {
        base.OnMinimumChanged(oldMinimum, newMinimum);
        InvalidateVisual();
    }

    protected override void OnMaximumChanged(double oldMaximum, double newMaximum)
    {
        base.OnMaximumChanged(oldMaximum, newMaximum);
        InvalidateVisual();
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        InvalidateVisual();
    }

    private static object CoerceRingThickness(DependencyObject dependencyObject, object baseValue) =>
        Math.Max(0.5, (double)baseValue);

    private static void OnAnimationStateChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is ProgressRing ring)
            ring.UpdateAnimation();
    }

    private void UpdateAnimation()
    {
        if (!IsLoaded || !IsVisible || !IsActive || !IsIndeterminate)
        {
            StopAnimation();
            return;
        }

        var animation = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromMilliseconds(920),
            RepeatBehavior = RepeatBehavior.Forever
        };
        BeginAnimation(RotationAngleProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void StopAnimation()
    {
        BeginAnimation(RotationAngleProperty, null);
        SetCurrentValue(RotationAngleProperty, 0d);
    }

    private static Pen CreatePen(Brush brush, double thickness) => new(brush, thickness)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round
    };

    private static void DrawArc(
        DrawingContext drawingContext,
        Pen pen,
        Point center,
        double radius,
        double startAngle,
        double sweepAngle)
    {
        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, startAngle + sweepAngle);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(
                end,
                new Size(radius, radius),
                0,
                sweepAngle > 180,
                SweepDirection.Clockwise,
                true,
                false);
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }
}
