using System.Windows.Controls;
using XFEToolBox.Client.Views.Windows;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.ViewModel.Pages.Popups;
using NormalDialogPopupPageViewModel = XFEToolBox.Client.ViewModel.Pages.Popups.NormalDialogPopupPageViewModel;

namespace XFEToolBox.Client.Views.Pages.Popups;

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
