using System.Windows.Controls;
using XFEToolBox.Client.Installer.ViewModel.Pages;

namespace XFEToolBox.Client.Installer.Views.Pages
{
    /// <summary>
    /// InstallPage.xaml 的交互逻辑
    /// </summary>
    public partial class InstallPage : Page
    {
        public static InstallPage? Current { get; set; }
        public InstallPageViewModel ViewModel { get; set; }
        public InstallPage()
        {
            ViewModel = new(Current = this);
            DataContext = ViewModel;
            InitializeComponent();
        }
    }
}
