using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;
using XFEToolBox.Client.Installer.Views.Windows;

namespace XFEToolBox.Client.Installer.ViewModel.Windows;

public partial class PopupWindowViewModel(PopupWindow viewPage) : ViewModelBase
{
    [ObservableProperty]
    private object? content;

    [ObservableProperty]
    private string popupTitle = "XFE工具箱安装程序";

    [ObservableProperty]
    private Visibility closeButtonVisibility = Visibility.Visible;

    [ObservableProperty]
    private Visibility moveButtonVisibility = Visibility.Visible;

    public PopupWindow ViewPage { get; } = viewPage;
}
