using CommunityToolkit.Mvvm.ComponentModel;
using XFEToolBox.Views.Pages;

namespace XFEToolBox.ViewModel.Pages;

public class DownloadPageViewModel(DownloadPage viewPage) : ObservableObject
{
    public DownloadPage ViewPage { get; set; } = viewPage;
}
