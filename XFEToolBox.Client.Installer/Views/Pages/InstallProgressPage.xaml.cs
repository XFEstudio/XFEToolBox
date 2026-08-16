using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using XFEToolBox.Client.Installer.Profiles;
using XFEToolBox.Client.Installer.Utilities;
using XFEToolBox.Client.Installer.ViewModel.Pages;

namespace XFEToolBox.Client.Installer.Views.Pages
{
    /// <summary>
    /// InstallProgressPage.xaml 的交互逻辑
    /// </summary>
    public partial class InstallProgressPage : Page
    {
        public static InstallProgressPage? Current { get; set; }
        public InstallProgressPageViewModel ViewModel { get; set; }
        public InstallProgressPage()
        {
            ViewModel = new(Current = this);
            DataContext = ViewModel;
            InitializeComponent();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await Task.Run(() =>
            {
                switch (SystemProfile.StartMode)
                {
                    case "Upgrade":
                        var filePath = Path.Combine(SystemProfile.InstallPath, "InstallPackage.zip");
                        if (File.Exists(filePath) && File.OpenRead(filePath) is FileStream fileStream)
                        {
                            Install(fileStream);
                            File.Delete(filePath);
                        }
                        break;
                    default:
                        if (Assembly.GetExecutingAssembly().GetManifestResourceStream("XFEToolBox.Client.Installer.Resources.Resource.Source.zip") is Stream innerStream)
                        {
                            if (SystemProfile.InstallPath != string.Empty && !Directory.Exists(SystemProfile.InstallPath))
                                Directory.CreateDirectory(SystemProfile.InstallPath);
                            if (Install(innerStream))
                                FileHelper.CreateShortCut($@"{Environment.GetFolderPath(Environment.SpecialFolder.Desktop)}\XFE工具箱.lnk", Path.Combine(SystemProfile.InstallPath, "XFEToolBox.exe"), null, "XFE工具箱快捷方式", null, SystemProfile.InstallPath);
                        }
                        break;
                }
            });
        }

        private bool Install(Stream stream)
        {
            try
            {
                ZipHelper.ExtraZipStream(stream, SystemProfile.InstallPath);
                Dispatcher.Invoke(() =>
                {
                    installGrid.Visibility = Visibility.Collapsed;
                    successGrid.Visibility = Visibility.Visible;
                });
                return true;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => progress.IsError = true);
                MessageBox.Show($"安装时发生错误：\n{ex.Message}");
                return false;
            }
        }
    }
}
