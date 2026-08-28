using System.IO;
using System.Windows;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Views.Pages.Popups;
using XFEToolBox.Client.Views.Windows;

namespace XFEToolBox.Client.Utilities;

internal static class ToolWorkshopService
{
    public static void ShowProjectLauncher(Window? owner = null)
    {
        var launcher = new ToolProjectLauncherPopupPage();
        var result = PopupHelper.ShowDialog(launcher, new PopupWindowOptions
        {
            Title = "工具项目",
            Subtitle = "选择历史项目或浏览本地项目",
            Width = 700,
            Height = 520,
            ContentMargin = new Thickness(0)
        });
        if (launcher.CreateProjectRequested)
        {
            ShowNewProjectDialog(owner);
            return;
        }

        if (result == MessageBoxResult.OK && launcher.SelectedProjectPath is not null)
            _ = OpenProjectAsync(launcher.SelectedProjectPath);
    }

    public static void ShowNewProjectDialog(Window? owner = null)
    {
        var creator = new NewToolProjectPopupPage();
        var result = PopupHelper.ShowDialog(creator, new PopupWindowOptions
        {
            Title = "新建工具",
            Subtitle = "创建标准 XFEToolBox 工具工程",
            Width = 620,
            Height = 430,
            ContentMargin = new Thickness(0)
        });
        if (result == MessageBoxResult.OK && creator.CreatedProjectPath is not null)
            _ = OpenProjectAsync(creator.CreatedProjectPath);
    }

    public static async Task<bool> OpenProjectAsync(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath)) return false;
        var fullPath = Path.GetFullPath(projectPath);
        await ToolProjectWorkspaceService.RememberProjectAsync(fullPath);
        var history = (await ToolProjectWorkspaceService.LoadHistoryAsync())
            .FirstOrDefault(item => item.ProjectPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        if (history is not null) RecentUsageService.RecordProject(history);

        var editor = new ToolCodeEditorWindow(fullPath);
        editor.Show();
        editor.Activate();
        return true;
    }

    public static async Task<bool> ContinueLastProjectAsync()
    {
        var project = (await ToolProjectWorkspaceService.LoadHistoryAsync()).FirstOrDefault(item => item.Exists);
        return project is not null && await OpenProjectAsync(project.ProjectPath);
    }
}
