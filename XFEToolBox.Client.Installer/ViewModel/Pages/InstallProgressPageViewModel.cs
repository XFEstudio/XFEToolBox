using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.IO;
using XFEToolBox.Client.Installer.Profiles;
using XFEToolBox.Client.Installer.ViewModel.Windows;
using XFEToolBox.Client.Installer.Views.Pages;

namespace XFEToolBox.Client.Installer.ViewModel.Pages
{
    public partial class InstallProgressPageViewModel(InstallProgressPage viewPage) : ViewModelBase
    {
        public InstallProgressPage ViewPage { get; set; } = viewPage;

        [RelayCommand]
        void ConfirmSuccess()
        {
            var startInfo = new ProcessStartInfo(Path.Combine(SystemProfile.InstallPath, SystemProfile.ApplicationExecutableName))
            {
                UseShellExecute = true,
                WorkingDirectory = SystemProfile.InstallPath
            };
            Process.Start(startInfo);
            MainWindowViewModel.CloseWindow();
        }
    }
}
