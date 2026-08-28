using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Views.Windows;

namespace XFEToolBox.Client.Views.Pages;

public partial class ToolWorkshopPage : Page
{
    private readonly ObservableCollection<ToolProjectHistoryItem> projects = [];

    public static ToolWorkshopPage Current { get; } = new();

    public ToolWorkshopPage()
    {
        InitializeComponent();
        ProjectList.ItemsSource = projects;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void NewProjectButton_Click(object sender, RoutedEventArgs e) =>
        ToolWorkshopService.ShowNewProjectDialog(Window.GetWindow(this));

    private void OpenProjectsButton_Click(object sender, RoutedEventArgs e) =>
        ToolWorkshopService.ShowProjectLauncher(Window.GetWindow(this));

    private void OpenGalleryButton_Click(object sender, RoutedEventArgs e) =>
        ControlGalleryWindow.ShowGallery(Window.GetWindow(this));

    private void ProjectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = ProjectList.SelectedItem is ToolProjectHistoryItem;
        OpenButton.IsEnabled = selected;
        RemoveButton.IsEnabled = selected;
    }

    private async void ProjectList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => await OpenSelectedAsync();

    private async void OpenButton_Click(object sender, RoutedEventArgs e) => await OpenSelectedAsync();

    private async void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectList.SelectedItem is not ToolProjectHistoryItem project) return;
        await ToolProjectWorkspaceService.RemoveProjectFromHistoryAsync(project.ProjectPath);
        await ReloadAsync();
    }

    private async Task OpenSelectedAsync()
    {
        if (ProjectList.SelectedItem is not ToolProjectHistoryItem project) return;
        if (!await ToolWorkshopService.OpenProjectAsync(project.ProjectPath))
        {
            StatusText.Text = "项目位置不存在，请移除记录或重新浏览。";
            return;
        }
        StatusText.Text = $"已打开 {project.Name}";
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        projects.Clear();
        foreach (var project in await ToolProjectWorkspaceService.LoadHistoryAsync()) projects.Add(project);
        EmptyState.Visibility = projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = projects.Count == 0 ? string.Empty : $"共 {projects.Count} 个最近项目";
    }
}
