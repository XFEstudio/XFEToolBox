using System.Windows.Controls;

namespace XFEToolBox.Views.Pages;

/// <summary>
/// SettingPage.xaml 的交互逻辑
/// </summary>
public partial class SettingPage : Page
{
    public static SettingPage? Current { get; set; } = new SettingPage();
    public SettingPage()
    {
        InitializeComponent();
        Current = this;
    }
}
