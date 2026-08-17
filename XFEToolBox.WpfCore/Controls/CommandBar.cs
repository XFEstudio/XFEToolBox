using System.Windows;
using System.Windows.Controls;

namespace XFEToolBox.Client.Views.Controls;

/// <summary>
/// 用于组织页面主命令、标题以及尾部辅助操作的横向命令栏。
/// </summary>
public class CommandBar : HeaderedItemsControl
{
    public static readonly DependencyProperty SecondaryContentProperty = DependencyProperty.Register(
        nameof(SecondaryContent), typeof(object), typeof(CommandBar), new PropertyMetadata(null));

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(CommandBar), new PropertyMetadata(false));

    public object? SecondaryContent
    {
        get => GetValue(SecondaryContentProperty);
        set => SetValue(SecondaryContentProperty, value);
    }

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }
}
