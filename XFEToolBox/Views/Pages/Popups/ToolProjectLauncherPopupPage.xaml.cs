using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Views.Windows;

namespace XFEToolBox.Client.Views.Pages.Popups;

public partial class ToolProjectLauncherPopupPage : Page, IPopupPage
{
    private readonly ObservableCollection<ToolProjectHistoryItem> projects = [];

    public ToolProjectLauncherPopupPage()
    {
        InitializeComponent();
        ProjectList.ItemsSource = projects;
    }

    public PopupWindow? PopupWindow { get; set; }
    public string? SelectedProjectPath { get; private set; }

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        projects.Clear();
        foreach (var project in await ToolProjectWorkspaceService.LoadHistoryAsync())
            projects.Add(project);
        EmptyState.Visibility = projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ProjectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = ProjectList.SelectedItem is ToolProjectHistoryItem;
        OpenButton.IsEnabled = selected;
        RemoveButton.IsEnabled = selected;
        StatusText.Text = string.Empty;
    }

    private async void ProjectList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProjectList.SelectedItem is ToolProjectHistoryItem)
            await OpenSelectedAsync();
    }

    private async void OpenButton_Click(object sender, RoutedEventArgs e) => await OpenSelectedAsync();

    private async Task OpenSelectedAsync()
    {
        if (ProjectList.SelectedItem is not ToolProjectHistoryItem project)
            return;
        if (!Directory.Exists(project.ProjectPath))
        {
            StatusText.Text = "项目位置不存在，请移除记录或重新浏览该项目。";
            return;
        }

        SelectedProjectPath = project.ProjectPath;
        await ToolProjectWorkspaceService.RememberProjectAsync(project.ProjectPath);
        if (PopupWindow is not null)
            await PopupWindow.CloseWithResultAsync(MessageBoxResult.OK);
    }

    private async void BrowseProjectButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择工具项目目录", Multiselect = false };
        if (dialog.ShowDialog(PopupWindow) != true)
            return;
        if (!File.Exists(Path.Combine(dialog.FolderName, "manifest.json")))
        {
            StatusText.Text = "所选目录中没有 manifest.json，无法识别为工具项目。";
            return;
        }

        SelectedProjectPath = Path.GetFullPath(dialog.FolderName);
        await ToolProjectWorkspaceService.RememberProjectAsync(SelectedProjectPath);
        if (PopupWindow is not null)
            await PopupWindow.CloseWithResultAsync(MessageBoxResult.OK);
    }

    private async void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectList.SelectedItem is not ToolProjectHistoryItem project)
            return;
        await ToolProjectWorkspaceService.RemoveProjectFromHistoryAsync(project.ProjectPath);
        await ReloadAsync();
    }
}
