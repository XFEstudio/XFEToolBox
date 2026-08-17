using System.Windows;
using XFEToolBox.Client.Installer.Profiles;

namespace XFEToolBox.Client.Installer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            SystemProfile.Args = e.Args;
            if (e.Args.Length == 0)
            {
                SystemProfile.FirstInstall = true;
            }
            else if (e.Args.Length == 3 &&
                     string.Equals(e.Args[0], "Upgrade", StringComparison.OrdinalIgnoreCase) &&
                     Uri.TryCreate(e.Args[1], UriKind.Absolute, out var parsedDownloadUri) &&
                     parsedDownloadUri is { } downloadUri &&
                     (downloadUri.Scheme == Uri.UriSchemeHttp || downloadUri.Scheme == Uri.UriSchemeHttps) &&
                     System.IO.Directory.Exists(e.Args[2]))
            {
                SystemProfile.StartMode = "Upgrade";
                SystemProfile.DownloadUrl = downloadUri.AbsoluteUri;
                SystemProfile.InstallPath = System.IO.Path.GetFullPath(e.Args[2]);
            }
            else
            {
                MessageBox.Show(
                    "Installer 收到的升级参数无效。请从 XFEToolBox 内重新检查更新。",
                    "无法开始升级",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(-1);
            }
        }
    }
}
