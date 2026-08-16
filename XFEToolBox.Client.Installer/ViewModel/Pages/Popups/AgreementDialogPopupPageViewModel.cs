using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XFEToolBox.Client.Installer.Utilities;
using XFEToolBox.Client.Installer.Views.Controls;
using XFEToolBox.Client.Installer.Views.Pages.Popups;

namespace XFEToolBox.Client.Installer.ViewModel.Pages.Popups
{
    public partial class AgreementDialogPopupPageViewModel(AgreementDialogPopupPage viewPage) : ViewModelBase
    {
        [ObservableProperty]
        string agreementContent = "";
        public AgreementDialogPopupPage ViewPage { get; set; } = viewPage;

        [RelayCommand]
        void Confirm()
        {
            if (ViewPage.PopupWindow is not null)
            {
                ViewPage.PopupWindow.Result = System.Windows.MessageBoxResult.Yes;
                ViewPage.PopupWindow.Close();
            }
        }
    }
}
