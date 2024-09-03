using System.Windows.Controls;
using XFEToolBox.ViewModel.Pages.Popups;
using XFEToolBox.Views.Windows;

namespace XFEToolBox.Views.Pages.Popups;

/// <summary>
/// NormalDialogPopupPage.xaml 的交互逻辑
/// </summary>
public partial class NormalDialogPopupPage : Page
{
    public NormalDialogPopupPageViewModel ViewModel { get; set; }
    public PopupWindow PopupWindow { get; set; }
    public NormalDialogPopupPage(PopupWindow popupWindow)
    {
        PopupWindow = popupWindow;
        DataContext = ViewModel = new(this);
        InitializeComponent();
    }
}
