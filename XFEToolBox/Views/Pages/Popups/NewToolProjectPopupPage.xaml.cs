using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Views.Windows;

namespace XFEToolBox.Client.Views.Pages.Popups;

public partial class NewToolProjectPopupPage : Page, IPopupPage
{
    private bool pathWasGenerated = true;
    private bool updatingGeneratedPath;
    private string projectParentRoot = ToolProjectWorkspaceService.DefaultProjectsRoot;
    private string previousGeneratedPath = string.Empty;

    public NewToolProjectPopupPage() => InitializeComponent();

    public PopupWindow? PopupWindow { get; set; }
    public string? CreatedProjectPath { get; private set; }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ProjectNameBox.Text = "NewToolProject";
        UpdateGeneratedPath();
        ProjectNameBox.Focus();
        ProjectNameBox.SelectAll();
    }

    private void ProjectNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(previousGeneratedPath)
            || ProjectPathBox.Text.Equals(previousGeneratedPath, StringComparison.OrdinalIgnoreCase))
            pathWasGenerated = true;
        if (pathWasGenerated)
            UpdateGeneratedPath();
    }

    private void UpdateGeneratedPath()
    {
        var name = string.IsNullOrWhiteSpace(ProjectNameBox.Text) ? "NewToolProject" : ProjectNameBox.Text.Trim();
        previousGeneratedPath = Path.Combine(projectParentRoot, name);
        updatingGeneratedPath = true;
        ProjectPathBox.Text = previousGeneratedPath;
        updatingGeneratedPath = false;
    }

    private void ProjectPathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!updatingGeneratedPath)
            pathWasGenerated = false;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择工具项目的父目录", Multiselect = false };
        if (dialog.ShowDialog(PopupWindow) != true)
            return;
        projectParentRoot = dialog.FolderName;
        pathWasGenerated = true;
        UpdateGeneratedPath();
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        CreateButton.IsEnabled = false;
        ErrorText.Text = string.Empty;
        try
        {
            CreatedProjectPath = await ToolProjectWorkspaceService.CreateProjectAsync(ProjectNameBox.Text, ProjectPathBox.Text);
            if (PopupWindow is not null)
                await PopupWindow.CloseWithResultAsync(MessageBoxResult.OK);
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            CreateButton.IsEnabled = true;
        }
    }

    private async void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (PopupWindow is not null)
            await PopupWindow.CloseWithResultAsync(MessageBoxResult.Cancel);
    }
}
