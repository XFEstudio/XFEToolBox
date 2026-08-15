using System.Windows.Controls;
using XFEToolBox.Client.ViewModel.Pages;

namespace XFEToolBox.Client.Views.Pages;

/// <summary>
/// ConsolePage.xaml 的交互逻辑
/// </summary>
public partial class ConsolePage : Page
{
    public static ConsolePage? Current { get; set; } = new();
    public ConsolePageViewModel ViewModel { get; set; }
    public ConsolePage()
    {
        Current = this;
        DataContext = ViewModel = new(this);
        InitializeComponent();
    }

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e) => ViewModel.ScrollChanged(sender, e);
}
