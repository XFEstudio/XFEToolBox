using System.Windows.Controls;
using XFEToolBox.Client.Views.Windows;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.ViewModel.Pages.Popups;
using AgreementDialogPopupPageViewModel = XFEToolBox.Client.ViewModel.Pages.Popups.AgreementDialogPopupPageViewModel;

namespace XFEToolBox.Client.Views.Pages.Popups;

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

    private void SmoothScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 1)
            ViewModel.ReadCheckButtonEnable = true;
    }
}
