using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.IO;
using XFEToolBox.Client.Installer.Profiles;
using XFEToolBox.Client.Installer.Utilities;
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
            var executablePath = Path.Combine(SystemProfile.InstallPath, SystemProfile.ApplicationExecutableName);
            if (!File.Exists(executablePath))
            {
                PopupHelper.ShowConfirmDialog($"无法启动 XFEToolBox：未找到 {SystemProfile.ApplicationExecutableName}。", confirmText: "确定");
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo(executablePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = SystemProfile.InstallPath
                };
                if (Process.Start(startInfo) is null)
                    throw new InvalidOperationException("系统未能创建应用进程。");
                MainWindowViewModel.CloseWindow();
            }
            catch (Exception exception)
            {
                PopupHelper.ShowConfirmDialog($"无法启动 XFEToolBox：\n{exception.Message}", confirmText: "确定");
            }
        }
    }
}
