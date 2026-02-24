using System.Windows.Controls;
using System.Windows.Media;
using XFEToolBox.ViewModel.Pages;
using XFEToolBox.Views.Controls;

namespace XFEToolBox.Views.Pages;

/// <summary>
/// MainPage.xaml 的交互逻辑
/// </summary>
public partial class MainPage : Page
{
    public static MainPage? Current { get; set; } = new();
    public MainPageViewModel ViewModel { get; set; }
    public MainPage()
    {
        Current = this;
        ViewModel = new(this);
        InitializeComponent();
    }
}
