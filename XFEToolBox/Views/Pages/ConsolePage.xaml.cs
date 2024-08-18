using System.Windows.Controls;
using XFEToolBox.ViewModel;

namespace XFEToolBox.Views.Pages;

/// <summary>
/// ConsolePage.xaml 的交互逻辑
/// </summary>
public partial class ConsolePage : Page
{
    public static ConsolePage? Current { get; set; } = new();
    public ConsolePageViewModel ViewModel { get; set; }
    public ConsolePage()
    {
        InitializeComponent();
        ViewModel = new ConsolePageViewModel(this);
        DataContext = ViewModel;
        Current = this;
    }

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e) => ViewModel.ScrollChanged(sender, e);
}
