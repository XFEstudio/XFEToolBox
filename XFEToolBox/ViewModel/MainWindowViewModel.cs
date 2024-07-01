using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Controls;

namespace FileSystemWatcher.ViewModel;

public partial class MainWindowViewModel : ObservableObject
{
    public MainWindow ViewPage { get; set; }

    [ObservableProperty]
    private Page? currentPage;


    public MainWindowViewModel(MainWindow viewPage)
    {
        ViewPage = viewPage;
    }

    public MainWindowViewModel()
    {
    }
}
