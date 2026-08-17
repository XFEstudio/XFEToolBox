using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XFEToolBox.WpfCore.Controls;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Views.Pages.Popups;

namespace XFEToolBox.Client.ViewModel.Pages.Popups;

public partial class AgreementDialogPopupPageViewModel(AgreementDialogPopupPage viewPage) : ObservableObject
{
    [ObservableProperty]
    string agreementContent = "";
    [ObservableProperty]
    bool acceptButtonEnable = false;
    [ObservableProperty]
    bool readCheckButtonEnable = false;
    public AgreementDialogPopupPage ViewPage { get; set; } = viewPage;

    [RelayCommand]
    void Agree()
    {
        if (ViewPage.PopupWindow is not null)
        {
            if (AcceptButtonEnable)
            {
                ViewPage.PopupWindow.Result = System.Windows.MessageBoxResult.Yes;
                ViewPage.PopupWindow.Close();
            }
            else
            {
                PopupHelper.ShowConfirmDialog("请先阅读以上内容并勾选 “我已知晓” 选项！");
            }
        }
    }

    [RelayCommand]
    void Refuse()
    {
        if (ViewPage.PopupWindow is not null)
        {
            ViewPage.PopupWindow.Result = System.Windows.MessageBoxResult.No;
            ViewPage.PopupWindow.Close();
        }
    }

    [RelayCommand]
    void ReadCheck(CheckButton checkButton) => AcceptButtonEnable = checkButton.IsChecked is true;
}
