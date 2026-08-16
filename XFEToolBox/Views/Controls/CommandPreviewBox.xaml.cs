using System.Windows;
using System.Windows.Controls;

namespace XFEToolBox.Client.Views.Controls;

/// <summary>
/// 统一展示图形化操作所对应的命令行，并提供一键复制。
/// 命令执行仍由调用方负责，本控件不会启动任何进程。
/// </summary>
public partial class CommandPreviewBox : UserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(CommandPreviewBox), new PropertyMetadata("等价命令"));

    public static readonly DependencyProperty CommandTextProperty = DependencyProperty.Register(
        nameof(CommandText), typeof(string), typeof(CommandPreviewBox), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CopyButtonTextProperty = DependencyProperty.Register(
        nameof(CopyButtonText), typeof(string), typeof(CommandPreviewBox), new PropertyMetadata("复制"));

    public CommandPreviewBox() => InitializeComponent();

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string CommandText
    {
        get => (string)GetValue(CommandTextProperty);
        set => SetValue(CommandTextProperty, value);
    }

    public string CopyButtonText
    {
        get => (string)GetValue(CopyButtonTextProperty);
        set => SetValue(CopyButtonTextProperty, value);
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(CommandText))
            Clipboard.SetText(CommandText);
    }
}
