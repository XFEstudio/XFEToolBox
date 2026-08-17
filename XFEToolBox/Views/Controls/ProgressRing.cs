using System.Windows;
using System.Windows.Controls;

namespace XFEToolBox.Client.Views.Controls;

/// <summary>
/// 与工具箱主题一致的环形忙碌指示器。
/// </summary>
public class ProgressRing : Control
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(ProgressRing), new PropertyMetadata(true));

    public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
        nameof(RingThickness), typeof(double), typeof(ProgressRing), new PropertyMetadata(3d));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public double RingThickness
    {
        get => (double)GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }
}
