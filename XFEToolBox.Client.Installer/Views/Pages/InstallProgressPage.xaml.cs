using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using XFEToolBox.Client.Installer.Profiles;
using XFEToolBox.Client.Installer.Utilities;
using XFEToolBox.Client.Installer.ViewModel.Pages;

namespace XFEToolBox.Client.Installer.Views.Pages;

public partial class InstallProgressPage : Page
{
    private const string PackageResourceName = "XFEToolBox.Client.Installer.Resources.Resource.Source.zip";
    private bool installationRunning;
    private bool installationSucceeded;

    public InstallProgressPageViewModel ViewModel { get; }

    public InstallProgressPage()
    {
        ViewModel = new InstallProgressPageViewModel(this);
        DataContext = ViewModel;
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!installationRunning && !installationSucceeded)
            await RunInstallationAsync();
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e) => await RunInstallationAsync();

    private async Task RunInstallationAsync()
    {
        if (installationRunning)
            return;

        installationRunning = true;
        ShowInstalling();
        try
        {
            var completionDetail = await Task.Run(InstallPackage);
            installationSucceeded = true;
            successDetailText.Text = completionDetail;
            installGrid.Visibility = Visibility.Collapsed;
            errorGrid.Visibility = Visibility.Collapsed;
            successGrid.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            installationSucceeded = false;
            progress.IsBusy = false;
            progress.IsError = true;
            progress.SetBusy();
            progress.SetError();
            errorMessageText.Text = $"{exception.Message}\n\n请检查安装目录权限和安装包完整性后重试。";
            installGrid.Visibility = Visibility.Collapsed;
            successGrid.Visibility = Visibility.Collapsed;
            errorGrid.Visibility = Visibility.Visible;
        }
        finally
        {
            installationRunning = false;
        }
    }

    private static string InstallPackage()
    {
        if (string.Equals(SystemProfile.StartMode, "Upgrade", StringComparison.OrdinalIgnoreCase))
            return InstallUpgradePackage();

        using var packageStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PackageResourceName)
                                  ?? throw new InvalidDataException("安装程序中缺少内置安装包 Source.zip，请重新下载安装器。");
        InstallationService.InstallPackage(packageStream, SystemProfile.InstallPath, SystemProfile.ApplicationExecutableName);

        var shortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "XFE工具箱.lnk");
        var shortcutCreated = FileHelper.CreateShortCut(
            shortcutPath,
            Path.Combine(SystemProfile.InstallPath, SystemProfile.ApplicationExecutableName),
            description: "XFE工具箱快捷方式",
            workingDirectory: SystemProfile.InstallPath);

        return shortcutCreated
            ? "XFEToolBox 已安装完成，并已创建桌面快捷方式。"
            : "XFEToolBox 已安装完成；桌面快捷方式创建失败，但不影响正常使用。";
    }

    private static string InstallUpgradePackage()
    {
        var packagePath = Path.Combine(SystemProfile.InstallPath, "InstallPackage.zip");
        if (!File.Exists(packagePath))
            throw new FileNotFoundException("未找到已下载的升级包，请返回 XFEToolBox 重新检查更新。", packagePath);

        using (var packageStream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            InstallationService.InstallPackage(packageStream, SystemProfile.InstallPath, SystemProfile.ApplicationExecutableName);

        File.Delete(packagePath);
        return "XFEToolBox 已升级完成，安装包验证通过并已清理临时文件。";
    }

    private void ShowInstalling()
    {
        installGrid.Visibility = Visibility.Visible;
        successGrid.Visibility = Visibility.Collapsed;
        errorGrid.Visibility = Visibility.Collapsed;
        progress.IsError = false;
        progress.IsBusy = true;
        progress.SetError();
        progress.SetBusy();
    }
}
