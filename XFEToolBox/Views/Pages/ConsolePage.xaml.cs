using System.Windows.Controls;
using XFEToolBox.Client.ViewModel.Pages;

namespace XFEToolBox.Client.Views.Pages;

/// <summary>
/// ConsolePage.xaml 的交互逻辑
/// </summary>
public partial class ConsolePage : Page
{
    private ScrollViewer? consoleScrollViewer;

    public static ConsolePage? Current { get; set; } = new();
    public ConsolePageViewModel ViewModel { get; set; }
    public ConsolePage()
    {
        Current = this;
        InitializeComponent();
        DataContext = ViewModel = new(this);
    }

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        consoleScrollViewer = sender as ScrollViewer;
        ViewModel.ScrollChanged(sender, e);
    }

    internal void ScrollConsoleToEnd()
    {
        if (consoleScrollViewer is not null)
        {
            consoleScrollViewer.ScrollToEnd();
            return;
        }

        if (consoleListBox.Items.Count > 0)
            consoleListBox.ScrollIntoView(consoleListBox.Items[^1]);
    }
}
