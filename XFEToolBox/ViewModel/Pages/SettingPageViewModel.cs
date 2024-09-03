using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XFEExtension.NetCore.FileExtension;
using XFEToolBox.Core.Model;
using XFEToolBox.Profiles.CrossVersionProfiles;
using XFEToolBox.Utilities;
using XFEToolBox.Views.Controls;
using XFEToolBox.Views.Pages;

namespace XFEToolBox.ViewModel.Pages;

public partial class SettingPageViewModel(SettingPage viewPage) : ObservableObject
{
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
    public SettingPage ViewPage { get; set; } = viewPage;

    public static void LoadSettingProfile(DependencyObject parent)
    {
        if (parent is null)
            return;
        int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childrenCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            ChildFound(child);
            LoadSettingProfile(child);
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
                case "XFEToolBox.Profiles.CrossVersionProfiles.ConsoleProfile.ConsolePort":
                    ReadWithDefaultValue(textBox, 3280);
                    break;
                case "XFEToolBox.Profiles.CrossVersionProfiles.ConsoleProfile.MaxLine":
                    ReadWithDefaultValue(textBox, 8000);
                    break;
                case "XFEToolBox.Profiles.CrossVersionProfiles.DownloadProfile.DownloadThread":
                    ReadWithDefaultValue(textBox, 9);
                    break;
                default:
                    textBox.Text = ProfileHelper.GetProfileValue<string>(textEditorTagPath);
                    break;
            }
        }
        else if (child is PasswordHintTextBox passwordHintTextBox && passwordHintTextBox.Tag is string passwordTagPath)
        {
            passwordHintTextBox.Password = ProfileHelper.GetProfileValue<string>(passwordTagPath) ?? string.Empty;
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
        if (e.OriginalSource is TextBox textBox && textBox.Name == "mainTextBox" && sender is TextEditor textEditor && textEditor.Tag is string commandPath)
        {
            switch (commandPath)
            {
                case "XFEToolBox.Profiles.CrossVersionProfiles.ConsoleProfile.ConsolePort":
                    SetWithDefaultValue(textEditor, 3280);
                    break;
                case "XFEToolBox.Profiles.CrossVersionProfiles.ConsoleProfile.MaxLine":
                    SetWithDefaultValue(textEditor, 8000);
                    break;
                case "XFEToolBox.Profiles.CrossVersionProfiles.DownloadProfile.DownloadThread":
                    SetWithDefaultValue(textEditor, 9);
                    break;
                default:
                    ProfileHelper.SetProfileValue(commandPath, textBox.Text);
                    break;
            }
        }
    }

    public void PasswordChange(object sender, PasswordChangeEventArgs e)
    {
        if (sender is PasswordHintTextBox passwordHintTextBox && passwordHintTextBox.Tag is string commandPath)
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
        if (parent is null)
            return;
        int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childrenCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TabUnderLineButton tabUnderLineButton && tabUnderLineButton.Tag is string tabTag && tabUnderLineButton.IsChecked is false && ViewPage.FindName($"{tabTag}SettingBlock") is TextBlock textBlock)
            {
                var target = textBlock.TranslatePoint(new(), ViewPage.scrollViewer).Y;
                if (target <= 0 && target >= -50)
                    tabUnderLineButton.IsChecked = true;
            }
            CheckTargetScrollTab(child);
        }
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
                case "SystemProfile.AutoSelfLaunch":

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
        Directory.Delete(AppPath.CacheProfile, true);
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
            ViewPage.scrollViewer.ScrollToVerticalOffset(ViewPage.scrollViewer.VerticalOffset + textBlock.TranslatePoint(new(), ViewPage.scrollViewer).Y - 20);
    }
    #endregion
}