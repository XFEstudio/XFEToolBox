using System.Windows.Controls;
using XFEToolBox.Client.Installer.Model;
using XFEToolBox.Client.Installer.ViewModel.Pages.Popups;
using XFEToolBox.Client.Installer.Views.Windows;

namespace XFEToolBox.Client.Installer.Views.Pages.Popups
{
    /// <summary>
    /// NormalDialogPopupPage.xaml 的交互逻辑
    /// </summary>
    public partial class NormalDialogPopupPage : Page, IPopupPage
    {
        public NormalDialogPopupPageViewModel ViewModel { get; set; }
        public PopupWindow? PopupWindow { get; set; }

        public NormalDialogPopupPage()
        {
            DataContext = ViewModel = new(this);
            InitializeComponent();
        }
    }
}
