using System.Windows.Controls;

namespace XFEToolBox.Views.Pages;

/// <summary>
/// ToolBoxPage.xaml 的交互逻辑
/// </summary>
public partial class ToolBoxPage : Page
{
    public static ToolBoxPage? Current { get; set; } = new();
    public ToolBoxPage()
    {
        InitializeComponent();
        Current = this;
    }
}
