using CommunityToolkit.Mvvm.ComponentModel;
using FileSystemWatcher.ViewPage;
using System.Windows.Controls;

namespace FileSystemWatcher.ViewModel;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private Page? currentPage = new MainPage();
}
