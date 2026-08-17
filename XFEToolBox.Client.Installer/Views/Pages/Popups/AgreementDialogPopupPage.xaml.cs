using System.Windows.Controls;
using XFEToolBox.Client.Installer.Model;
using XFEToolBox.Client.Installer.ViewModel.Pages.Popups;
using XFEToolBox.Client.Installer.Views.Windows;

namespace XFEToolBox.Client.Installer.Views.Pages.Popups
{
    /// <summary>
    /// AgreementDialogPopupPage.xaml 的交互逻辑
    /// </summary>
    public partial class AgreementDialogPopupPage : Page, IPopupPage
    {
        public AgreementDialogPopupPageViewModel ViewModel { get; set; }
        public PopupWindow? PopupWindow { get; set; }
        public string Agreement { get => ViewModel.AgreementContent; set => ViewModel.AgreementContent = value; }
        public AgreementDialogPopupPage()
        {
            DataContext = ViewModel = new(this);
            InitializeComponent();
        }
    }
}
