using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Windows;
using System.Windows.Media;
using XFEExtension.NetCore.FileExtension;
using XFEToolBox.Core.Model;
using XFEToolBox.Utilities;
using XFEToolBox.Views.Controls;
using XFEToolBox.Views.Pages;

namespace XFEToolBox.ViewModel;

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
    public SettingPage ViewPage { get; set; } = viewPage;

    public static void LoadSettingProfile(DependencyObject parent)
    {
        if (parent is null)
            return;
        int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childrenCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is SwitchButton switchButton && switchButton.Tag is string tagString)
            {
                switchButton.IsChecked = ProfileHelper.GetProfileValue<bool>(tagString);
            }
            LoadSettingProfile(child);
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
    #endregion
}