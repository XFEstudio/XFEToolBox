using System.Windows.Controls;
using XFEExtension.NetCore.FileExtension;
using XFEToolBox.Utilities;

namespace XFEToolBox.Views.Pages;

/// <summary>
/// MainPage.xaml 的交互逻辑
/// </summary>
public partial class MainPage : Page
{
    public static MainPage? Current { get; set; } = new();
    public MainPage()
    {
        InitializeComponent();
        Current = this;
    }

    private void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        //notifyText.Text = FileHelper.GetDirectorySize(new(@"C:\Users\XFEstudio\Downloads")).FileSize();
    }
}
