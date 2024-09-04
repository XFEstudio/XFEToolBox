using System.Windows;
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
    private PopupWindow? popupWindow;

    public PopupWindow? PopupWindow
    {
        get { return popupWindow; }
        set { popupWindow = value; }
    }

    public NormalDialogPopupPage()
    {
        DataContext = ViewModel = new(this);
        InitializeComponent();
    }
}
