using System.Windows;
using System.Windows.Controls;

namespace XFEToolBox.Client.Views.Controls;

/// <summary>
/// 左侧导航式分页控件，导航列表与当前子页拥有独立的滚动区域。
/// </summary>
public class NavigationView : TabControl
{
    public static readonly DependencyProperty NavigationWidthProperty = DependencyProperty.Register(
        nameof(NavigationWidth),
        typeof(GridLength),
        typeof(NavigationView),
        new FrameworkPropertyMetadata(new GridLength(190)));

    public GridLength NavigationWidth
    {
        get => (GridLength)GetValue(NavigationWidthProperty);
        set => SetValue(NavigationWidthProperty, value);
    }
}
