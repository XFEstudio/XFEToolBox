using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using XFEToolBox.Client.Models;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.ViewModel.Pages;
using XFEToolBox.Client.Views.Windows;

namespace XFEToolBox.Client.Views.Pages;

public partial class ToolWorkshopPage : Page
{
    private readonly ObservableCollection<ToolProjectCardViewModel> projects = [];

    public static ToolWorkshopPage Current { get; } = new();

    public ToolWorkshopPage()
    {
        InitializeComponent();
        ProjectList.DataContext = projects;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await ReloadAsync();
    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void NewProjectButton_Click(object sender, RoutedEventArgs e) =>
        ToolWorkshopService.ShowNewProjectDialog(Window.GetWindow(this));

    private void OpenProjectsButton_Click(object sender, RoutedEventArgs e) =>
        ToolWorkshopService.ShowProjectLauncher(Window.GetWindow(this));

    private void OpenGalleryButton_Click(object sender, RoutedEventArgs e) =>
        ControlGalleryWindow.ShowGallery(Window.GetWindow(this));

    private async void ProjectCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: ToolProjectCardViewModel project })
            await OpenProjectAsync(project);
    }

    private void TogglePinnedProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: ToolProjectCardViewModel project }) return;
        if (!project.TogglePinned())
            StatusText.Text = $"最多只能固定 {PinnedItemService.MaximumPinnedItems} 项";
        else
            StatusText.Text = project.IsPinned ? $"已固定 {project.Name}" : $"已取消固定 {project.Name}";
    }

    private async void RemoveProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: ToolProjectCardViewModel project }) return;
        await ToolProjectWorkspaceService.RemoveProjectFromHistoryAsync(project.ProjectPath);
        await ReloadAsync();
    }

    private void CopyProjectPathMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: ToolProjectCardViewModel project }) return;
        Clipboard.SetText(project.ProjectPath);
        StatusText.Text = $"已复制 {project.Name} 的路径";
    }

    private async Task OpenProjectAsync(ToolProjectCardViewModel project)
    {
        if (!await ToolWorkshopService.OpenProjectAsync(project.ProjectPath))
        {
            StatusText.Text = "项目位置不存在，可通过右键菜单移除记录。";
            return;
        }
        StatusText.Text = $"已打开 {project.Name}";
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        projects.Clear();
        var cards = (await ToolProjectWorkspaceService.LoadHistoryAsync())
            .Select(project => new ToolProjectCardViewModel(project))
            .ToArray();
        foreach (var card in cards) projects.Add(card);
        EmptyState.Visibility = projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = projects.Count == 0 ? string.Empty : $"共 {projects.Count} 个最近项目";
        await Task.WhenAll(cards.Select(card => card.IconLoadingTask));
    }
}
