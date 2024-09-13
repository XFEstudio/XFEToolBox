using CommunityToolkit.Mvvm.ComponentModel;
using XFEToolBox.Views.Pages;

namespace XFEToolBox.ViewModel.Pages;

public partial class DownloadInfoPageViewModel(DownloadInfoPage viewPage) : ObservableObject
{
    [ObservableProperty]
    string appTitle = "";
    public DownloadInfoPage ViewPage { get; set; } = viewPage;

}
