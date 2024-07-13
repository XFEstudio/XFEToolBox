using System.Windows.Controls;

namespace XFEToolBox.Views.Pages;

/// <summary>
/// MainPage.xaml 的交互逻辑
/// </summary>
public partial class MainPage : Page
{
    public static MainPage? Current { get; set; } = new MainPage();
    public MainPage()
    {
        InitializeComponent();
        Current = this;
    }
}
