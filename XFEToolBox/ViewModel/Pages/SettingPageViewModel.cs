using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using XFEExtension.NetCore.FileExtension;
using XFEToolBox.WpfCore.Controls;
using XFEToolBox.Core.Model;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Views.Pages;

namespace XFEToolBox.Client.ViewModel.Pages;

public partial class SettingPageViewModel : ObservableObject
{
    public SettingPageViewModel(SettingPage viewPage)
    {
        ViewPage = viewPage;
        launcherHotkeyText = SystemProfile.LauncherHotkey;
        globalHotkeyStatus = (Application.Current as App)?.GlobalHotkeyStatus ?? "快捷键服务尚未初始化";
        if (Application.Current is App app)
            app.GlobalHotkeyStatusChanged += (_, _) => RefreshHotkeyStatus();
    }

    [ObservableProperty]
    string cacheProfileSize = "计算中...";
    [ObservableProperty]
    string crossVersionProfileSize = "计算中...";
    [ObservableProperty]
    string currentVersionProfileSize = "计算中...";
    [ObservableProperty]
    string synchronizeProfileSize = "计算中...";
    [ObservableProperty]
    string totalProfileSize = "计算中...";
    [ObservableProperty]
    string downloadDirectory = "目标下载目录：";
    [ObservableProperty]
    string upgradeStatus = "可手动检查更新，也可在启动时自动检测新版本。";
    [ObservableProperty]
    string ignoredUpgradeVersionDisplay = GetIgnoredUpgradeVersionDisplay();
    [ObservableProperty]
    string launcherHotkeyText;
    [ObservableProperty]
    string globalHotkeyStatus;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckUpgradeCommand))]
    bool isCheckingForUpdates;
    bool ignoreNextScroll = false;
    public SettingPage ViewPage { get; }
    public string CurrentApplicationVersion => $"当前版本 {UpgradeHelper.DisplayVersion}";

    public static void LoadSettingProfile(DependencyObject parent)
    {
        if (parent is null)
            return;
        LoadSettingProfile(parent, []);
    }

    private static void LoadSettingProfile(DependencyObject parent, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(parent))
            return;

        ChildFound(parent);
        if (parent is SettingsExpander settingsExpander)
        {
            if (settingsExpander.Content is DependencyObject headerContent)
                LoadSettingProfile(headerContent, visited);
            foreach (var item in settingsExpander.Items)
                if (item is DependencyObject settingsItem)
                    LoadSettingProfile(settingsItem, visited);
        }
        else if (parent is SettingsCard { Content: DependencyObject cardContent })
        {
            LoadSettingProfile(cardContent, visited);
        }

        int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childrenCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            LoadSettingProfile(child, visited);
        }
    }

    public static void ChildFound(DependencyObject child)
    {
        if (child is SwitchButton switchButton && switchButton.Tag is string switchButtonTagPath)
        {
            switchButton.IsChecked = ProfileHelper.GetProfileValue<bool>(switchButtonTagPath);
        }
        else if (child is TextBox textBox && textBox.Tag is string textEditorTagPath)
        {
            switch (textEditorTagPath)
            {
                case "XFEToolBox.Client.Profiles.CrossVersionProfiles.ConsoleProfile.ConsolePort":
                    ReadWithDefaultValue(textBox, 3280);
                    break;
                case "XFEToolBox.Client.Profiles.CrossVersionProfiles.ConsoleProfile.MaxLine":
                    ReadWithDefaultValue(textBox, 8000);
                    break;
                case "XFEToolBox.Client.Profiles.CrossVersionProfiles.DownloadProfile.DownloadThread":
                    ReadWithDefaultValue(textBox, 9);
                    break;
                default:
                    textBox.Text = ProfileHelper.GetProfileValue<string>(textEditorTagPath);
                    break;
            }
        }
        else if (child is PasswordEditor passwordEditor && passwordEditor.Tag is string passwordTagPath)
        {
            passwordEditor.Password = ProfileHelper.GetProfileValue<string>(passwordTagPath) ?? string.Empty;
        }
    }

    private static void ReadWithDefaultValue<T>(TextBox textBox, T defaultValue) where T : struct
    {
        if (textBox.Tag is string commandPath)
        {
            var value = ProfileHelper.GetProfileValue<T>(commandPath);
            textBox.Text = value.Equals(defaultValue) ? string.Empty : value.ToString();
        }
    }

    private static void SetWithDefaultValue<T>(TextEditor textEditor, T defaultValue) where T : struct
    {
        if (textEditor.Tag is string commandPath)
        {
            if (int.TryParse(textEditor.Text, out var currentValue))
            {
                ProfileHelper.SetProfileValue(commandPath, currentValue);
            }
            else if (textEditor.Text == string.Empty || textEditor.Text is null)
            {
                ProfileHelper.SetProfileValue(commandPath, defaultValue);
            }
        }
    }

    public void TextChange(object sender, TextChangedEventArgs e)
    {
        if (sender is TextEditor textEditor && textEditor.Tag is string commandPath)
        {
            switch (commandPath)
            {
                case "XFEToolBox.Client.Profiles.CrossVersionProfiles.ConsoleProfile.ConsolePort":
                    SetWithDefaultValue(textEditor, 3280);
                    break;
                case "XFEToolBox.Client.Profiles.CrossVersionProfiles.ConsoleProfile.MaxLine":
                    SetWithDefaultValue(textEditor, 8000);
                    break;
                case "XFEToolBox.Client.Profiles.CrossVersionProfiles.DownloadProfile.DownloadThread":
                    SetWithDefaultValue(textEditor, 9);
                    break;
                default:
                    ProfileHelper.SetProfileValue(commandPath, textEditor.Text);
                    break;
            }
        }
    }

    public void PasswordChanged(object sender, PasswordChangedEventArgs e)
    {
        if (sender is PasswordEditor passwordEditor && passwordEditor.Tag is string commandPath)
        {
            switch (commandPath)
            {
                default:
                    ProfileHelper.SetProfileValue(commandPath, e.Password);
                    break;
            }
        }
    }

    public void ScrollChanged(object sender, ScrollChangedEventArgs e) => CheckTargetScrollTab(ViewPage);

    public void CheckTargetScrollTab(DependencyObject parent)
    {
        if (!ignoreNextScroll)
        {
            var results = FindType<TabUnderLineButton>(parent);
            var bestResult = results.First();
            double mostNearDistance = double.MinValue;
            foreach (var tabUnderLineButton in results)
            {
                if (tabUnderLineButton.Tag is string tabTag && ViewPage.FindName($"{tabTag}SettingBlock") is TextBlock textBlock)
                {
                    var target = textBlock.TranslatePoint(new(), ViewPage.scrollViewer).Y;
                    if (target <= 20 && target > mostNearDistance)
                    {
                        mostNearDistance = target;
                        bestResult = tabUnderLineButton;
                    }
                }
            }
            bestResult.IsChecked = true;
        }
        else
        {
            ignoreNextScroll = false;
        }
    }

    public List<T> FindType<T>(DependencyObject parent) where T : DependencyObject
    {
        var list = new List<T>();
        if (parent is null)
            return list;
        int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childrenCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T tChild)
                list.Add(tChild);
            else
                list.AddRange(FindType<T>(child));
        }
        return list;
    }

    public void CalculateFileSize()
    {
        var cacheProfileSize = FileHelper.GetDirectorySize(new(AppPath.CacheProfile));
        var crossVersionProfileSize = FileHelper.GetDirectorySize(new(AppPath.AppLocalData));
        var currentVersionProfileSize = FileHelper.GetDirectorySize(new(AppPath.LocalVersionProfile));
        var synchronizeProfileSize = FileHelper.GetDirectorySize(new(AppPath.SynProfile));
        CacheProfileSize = cacheProfileSize.FileSize();
        CrossVersionProfileSize = (crossVersionProfileSize - currentVersionProfileSize).FileSize();
        CurrentVersionProfileSize = currentVersionProfileSize.FileSize();
        SynchronizeProfileSize = synchronizeProfileSize.FileSize();
        TotalProfileSize = (cacheProfileSize + crossVersionProfileSize + synchronizeProfileSize).FileSize();
    }

    #region Command
    [RelayCommand]
    static void Switched(SwitchButton value)
    {
        if (value.IsChecked.HasValue)
        {
            var commandPath = value.Tag as string;
            switch (commandPath)
            {
                case "XFEToolBox.Client.Profiles.CrossVersionProfiles.SystemProfile.AutoSelfLaunch":
                    StartupRegistrationService.SetEnabled(value.IsChecked.Value);
                    break;
                default:
                    break;
            }
            ProfileHelper.SetProfileValue(commandPath!, value.IsChecked.Value);
        }
    }

    [RelayCommand]
    async Task ClearCache()
    {
        await TaskManager.Run(() => Directory.Delete(AppPath.CacheProfile, true), "正在清理缓存");
        await Task.Run(CalculateFileSize);
    }

    [RelayCommand]
    static void OpenDownloadDirectory() => Process.Start("explorer.exe", DownloadProfile.DownloadDirectory);

    [RelayCommand]
    void ChoiceDownloadDirectory()
    {
        var directoryOpenDialog = new OpenFolderDialog()
        {
            DefaultDirectory = DownloadProfile.DownloadDirectory,
            Multiselect = false,
            Title = "选择下载的目标文件夹位置"
        };
        var result = directoryOpenDialog.ShowDialog();
        if (result is true)
        {
            DownloadProfile.DownloadDirectory = directoryOpenDialog.FolderName;
            DownloadDirectory = $"下载目录：{DownloadProfile.DownloadDirectory}";
        }
    }

    [RelayCommand]
    void TabClicked(TabUnderLineButton value)
    {
        if (value.Tag is string tabTag && ViewPage.FindName($"{tabTag}SettingBlock") is TextBlock textBlock)
        {
            ignoreNextScroll = true;
            ViewPage.scrollViewer.ScrollToVerticalOffset(ViewPage.scrollViewer.VerticalOffset + textBlock.TranslatePoint(new(), ViewPage.scrollViewer).Y - 20);
        }
    }

    private bool CanCheckUpgrade() => !IsCheckingForUpdates;

    public void RefreshHotkeyStatus() => GlobalHotkeyStatus =
        (Application.Current as App)?.GlobalHotkeyStatus ?? "快捷键服务尚未初始化";

    [RelayCommand]
    private void ApplyLauncherHotkey()
    {
        if (!GlobalHotkeyService.TryNormalize(LauncherHotkeyText, out var normalized, out var error))
        {
            GlobalHotkeyStatus = error;
            return;
        }

        LauncherHotkeyText = normalized;
        if (Application.Current is App app) app.ConfigureGlobalHotkey(normalized);
        RefreshHotkeyStatus();
    }

    [RelayCommand(CanExecute = nameof(CanCheckUpgrade))]
    async Task CheckUpgrade()
    {
        IsCheckingForUpdates = true;
        UpgradeStatus = "正在连接升级服务器...";
        try
        {
            var result = await UpgradeService.CheckForUpdatesAsync(
                userInitiated: true,
                owner: Window.GetWindow(ViewPage));
            UpgradeStatus = result.Message;
            IgnoredUpgradeVersionDisplay = GetIgnoredUpgradeVersionDisplay();
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    void ClearIgnoredUpgradeVersion()
    {
        SystemProfile.IgnoredUpgradeVersion = string.Empty;
        IgnoredUpgradeVersionDisplay = GetIgnoredUpgradeVersionDisplay();
        UpgradeStatus = "已清除忽略记录，后续检查会再次提示所有新版本。";
    }

    private static string GetIgnoredUpgradeVersionDisplay() =>
        string.IsNullOrWhiteSpace(SystemProfile.IgnoredUpgradeVersion)
            ? "未忽略任何版本"
            : $"已忽略 {SystemProfile.IgnoredUpgradeVersion}";
    #endregion
}
