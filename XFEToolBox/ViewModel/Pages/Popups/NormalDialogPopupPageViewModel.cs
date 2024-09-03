using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using XFEExtension.NetCore.TaskExtension;
using XFEToolBox.Views.Pages.Popups;

namespace XFEToolBox.ViewModel.Pages.Popups;

public partial class NormalDialogPopupPageViewModel(NormalDialogPopupPage viewPage) : ObservableObject
{
    [ObservableProperty]
    string confirmText = "确认";
    [ObservableProperty]
    string cancelText = "取消";
    [ObservableProperty]
    object? content;
    public NormalDialogPopupPage ViewPage { get; set; } = viewPage;
    [RelayCommand]
    void Confirm()
    {
        ViewPage.PopupWindow.Result = MessageBoxResult.OK;
        ViewPage.PopupWindow.Close();
    }

    [RelayCommand]
    void Cancel()
    {
        ViewPage.PopupWindow.Result = MessageBoxResult.Cancel;
        ViewPage.PopupWindow.Close();
    }
}
