using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XFEToolBox.Views.Pages;

namespace XFEToolBox.ViewModel.Pages;

public partial class DownloadInfoPageViewModel(DownloadInfoPage viewPage) : ObservableObject
{
    [ObservableProperty]
    string appTitle = "";
    [ObservableProperty]
    string downloadButtonName = "下载";
    public DownloadInfoPage ViewPage { get; set; } = viewPage;

    [RelayCommand]
    void DownloadClick()
    {

    }
}
