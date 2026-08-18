using System.Windows;
using XFEToolBox.Client.Installer.ViewModel.Windows;
using XFEToolBox.WpfCore.Windowing;

namespace XFEToolBox.Client.Installer.Views.Windows;

public partial class PopupWindow : Window
{
    public PopupWindowViewModel ViewModel { get; }
    public MessageBoxResult? Result { get; set; }

    public PopupWindow()
    {
        ViewModel = new PopupWindowViewModel(this);
        DataContext = ViewModel;
        InitializeComponent();
        WindowWorkAreaHelper.Attach(this);
    }

    private void CaptionBar_CloseRequested(object? sender, EventArgs e)
    {
        Result = MessageBoxResult.None;
        Close();
    }
}
