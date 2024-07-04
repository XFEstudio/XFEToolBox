using CommunityToolkit.Mvvm.ComponentModel;
using XFEToolBox.Views.Pages;
using XFEToolBox.Views.Windows;
using System.Windows.Controls;

namespace XFEToolBox.ViewModel;

public partial class MainWindowViewModel : ObservableObject
{
    public MainWindow? ViewPage { get; set; }

    [ObservableProperty]
    private Page? currentPage = new MainPage();

    public MainWindowViewModel(MainWindow viewPage)
    {
        ViewPage = viewPage;
    }
}
