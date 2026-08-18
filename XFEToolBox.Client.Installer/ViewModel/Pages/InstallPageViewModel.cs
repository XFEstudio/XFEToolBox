using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.IO;
using System.Reflection;
using XFEToolBox.Client.Installer.Profiles;
using XFEToolBox.Client.Installer.Utilities;
using XFEToolBox.Client.Installer.Views.Pages;
using XFEToolBox.Client.Installer.Views.Pages.Popups;
using XFEToolBox.Client.Installer.Views.Windows;

namespace XFEToolBox.Client.Installer.ViewModel.Pages
{
    public partial class InstallPageViewModel(InstallPage viewPage) : ViewModelBase
    {
        [ObservableProperty]
        bool agreementChecked = false;
        [ObservableProperty]
        string installPath = SystemProfile.InstallPath;
        public InstallPage ViewPage { get; set; } = viewPage;

        [RelayCommand]
        void GotoInstallProgressPage()
        {
            try
            {
                var normalizedPath = Path.GetFullPath(InstallPath.Trim());
                if (FileHelper.IsRootPath(normalizedPath))
                    throw new InvalidOperationException("不能直接安装到磁盘根目录，请选择一个应用文件夹。");

                Directory.CreateDirectory(normalizedPath);
                VerifyWriteAccess(normalizedPath);
                InstallPath = normalizedPath;
                SystemProfile.InstallPath = normalizedPath;

                if (MainWindow.Current is not null)
                    MainWindow.Current.contentFrame.Content = new InstallProgressPage();
            }
            catch (Exception exception)
            {
                PopupHelper.ShowConfirmDialog($"安装目录不可用：\n{exception.Message}", confirmText: "重新选择");
            }
        }

        [RelayCommand]
        void ChangeInstallPath()
        {
            var openFolderDialog = new OpenFolderDialog
            {
                RootDirectory = InstallPath,
                Multiselect = false
            };
            if (openFolderDialog.ShowDialog() == true)
            {
                if (FileHelper.IsRootPath(openFolderDialog.FolderName))
                    InstallPath = Path.Combine(Path.GetFullPath(openFolderDialog.FolderName), SystemProfile.ApplicationName);
                else
                    InstallPath = Path.GetFullPath(openFolderDialog.FolderName);
                SystemProfile.InstallPath = InstallPath;
            }
        }

        [RelayCommand]
        void ViewEULAAgreement()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("XFEToolBox.Client.Installer.Resources.Resource.EULA.txt");
            if (stream is not null)
            {
                using var reader = new StreamReader(stream);
                PopupHelper.ShowDialog(new AgreementDialogPopupPage
                {
                    Title = "软件最终用户许可协议",
                    Agreement = reader.ReadToEnd()
                }, 480, 420);
            }
        }

        [RelayCommand]
        void ViewUserPrivateAgreement()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("XFEToolBox.Client.Installer.Resources.Resource.PrivateService.txt");
            if (stream is not null)
            {
                using var reader = new StreamReader(stream);
                PopupHelper.ShowDialog(new AgreementDialogPopupPage
                {
                    Title = "用户隐私协议",
                    Agreement = reader.ReadToEnd()
                }, 480, 420);
            }
        }

        private static void VerifyWriteAccess(string directory)
        {
            var probePath = Path.Combine(directory, $".xfe-write-test-{Guid.NewGuid():N}.tmp");
            try
            {
                using var probe = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                probe.WriteByte(0);
            }
            finally
            {
                if (File.Exists(probePath))
                    File.Delete(probePath);
            }
        }
    }
}
