using System.Windows.Controls;
using XFEToolBox.Model;
using XFEToolBox.ViewModel.Pages.Popups;
using XFEToolBox.Views.Windows;

namespace XFEToolBox.Views.Pages.Popups;

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
        if (e.VerticalOffset + e.ViewportHeight == e.ExtentHeight)
            ViewModel.ReadCheckButtonEnable = true;
    }
}
