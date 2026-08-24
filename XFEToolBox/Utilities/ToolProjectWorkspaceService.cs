using System.IO;
using System.Security.Cryptography;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using XFEToolBox.Core.Model;
using XFEToolBox.Core.Tools;

namespace XFEToolBox.Client.Utilities;

internal static class ToolProjectWorkspaceService
{
    private const string DefaultToolIconRelativePath = "Assets/icon.png";
    private const string DefaultToolIconResourcePath = "Resources/Image/default_tool_icon.png";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string DefaultProjectsRoot => Path.Combine(AppPath.AppLocalData, "EditorWorkspaces");

    private static string HistoryPath => Path.Combine(DefaultProjectsRoot, "project-history.json");

    public static async Task<string> CreateProjectAsync(string projectName, string projectPath)
    {
        projectName = projectName.Trim();
        if (string.IsNullOrWhiteSpace(projectName))
            throw new InvalidOperationException("请输入工具名称。");
        if (projectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("工具名称包含不能用于文件夹名称的字符。");

        var root = Path.GetFullPath(projectPath.Trim());
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidOperationException("项目位置不是空文件夹，请选择其他位置。");

        var viewsDirectory = Path.Combine(root, "Code", "Views");
        var modelsDirectory = Path.Combine(root, "Code", "Models");
        var viewModelsDirectory = Path.Combine(root, "Code", "ViewModels");
        var assetsDirectory = Path.Combine(root, "Assets");
        Directory.CreateDirectory(viewsDirectory);
        Directory.CreateDirectory(modelsDirectory);
        Directory.CreateDirectory(viewModelsDirectory);
        Directory.CreateDirectory(assetsDirectory);

        var safeId = new string(projectName.ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray()).Trim('-');
        if (string.IsNullOrWhiteSpace(safeId))
            safeId = "new-tool-project";
        var className = new string(projectName.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(className) || !char.IsLetter(className[0]))
            className = "NewToolProject";
        var toolNamespace = $"XFEToolBox.Tools.{className}";
        var jsonProjectName = JsonSerializer.Serialize(projectName);
        var xamlProjectName = SecurityElement.Escape(projectName) ?? projectName;

        var templates = new Dictionary<string, string>
        {
            ["manifest.json"] = $$"""
                {
                  "packageFormatVersion": 1,
                  "id": "local.{{safeId}}",
                  "name": {{jsonProjectName}},
                  "subtitle": "WPF 独立工具",
                  "version": "1.0.0",
                  "description": "请在这里填写工具说明。",
                  "author": "XFEstudio",
                  "icon": "{{DefaultToolIconRelativePath}}",
                  "category": "开发工具",
                  "tags": [ "WPF" ],
                  "requiresAdministrator": false,
                  "entry": {
                    "viewXaml": "Code/Views/MainPage.xaml",
                    "viewClass": "{{toolNamespace}}.MainPage",
                    "viewCodeBehind": "Code/Views/MainPage.xaml.cs",
                    "viewModel": "Code/ViewModels/MainPageViewModel.cs",
                    "viewModelClass": "{{toolNamespace}}.MainPageViewModel"
                  },
                  "window": {
                    "width": 760,
                    "height": 560,
                    "minWidth": 420,
                    "minHeight": 300,
                    "allowResize": true,
                    "allowMaximize": true,
                    "showMinimizeButton": true,
                    "showCloseButton": true
                  },
                  "requestedPermissions": []
                }
                """,
            [Path.Combine("Code", "Views", "MainPage.xaml")] = $$"""
                <UserControl x:Class="{{toolNamespace}}.MainPage"
                             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                    <Grid Margin="24">
                        <TextBlock Text="在这里编写工具界面" />
                    </Grid>
                </UserControl>
                """,
            [Path.Combine("Code", "Views", "MainPage.xaml.cs")] = $$"""
                using System.Windows;
                using System.Windows.Controls;

                namespace {{toolNamespace}};

                public partial class MainPage : UserControl
                {
                    public MainPage()
                    {
                        InitializeComponent();
                        DataContext = new MainPageViewModel();
                        Unloaded += OnUnloaded;
                    }

                    private void OnUnloaded(object sender, RoutedEventArgs e) =>
                        ((MainPageViewModel)DataContext).SaveSettings();
                }
                """,
            [Path.Combine("Code", "ViewModels", "MainPageViewModel.cs")] = $$"""
                using CommunityToolkit.Mvvm.ComponentModel;
                using CommunityToolkit.Mvvm.Input;
                using XFEToolBox.Core.Tools;

                namespace {{toolNamespace}};

                public partial class MainPageViewModel : ObservableObject
                {
                    [ObservableProperty]
                    private string result = string.Empty;

                    public MainPageViewModel()
                    {
                        if (ToolDataStore.IsInitialized)
                            result = ToolDataStore.Read("settings", new ToolSettings(string.Empty)).LastResult;
                    }

                    public void SaveSettings()
                    {
                        if (ToolDataStore.IsInitialized)
                            ToolDataStore.Write("settings", new ToolSettings(Result));
                    }

                    [RelayCommand]
                    private void Execute() => Result = "工具执行成功";

                    private sealed record ToolSettings(string LastResult);
                }
                """,
            [Path.Combine("Code", "Models", "ToolModel.cs")] = $$"""
                namespace {{toolNamespace}};

                public sealed class ToolModel
                {
                }
                """,
            ["README.md"] = $"# {projectName}\n\n使用 XFEToolBox Code Studio 编辑并发布此工具。\n"
        };

        foreach (var (relativePath, content) in templates)
        {
            var output = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, content, new UTF8Encoding(false));
        }

        await WriteDefaultToolIconAsync(Path.Combine(root, DefaultToolIconRelativePath));

        await RememberProjectAsync(root);
        return root;
    }

    public static async Task<IReadOnlyList<ToolProjectHistoryItem>> LoadHistoryAsync()
    {
        try
        {
            var hasSavedHistory = File.Exists(HistoryPath);
            var items = hasSavedHistory
                ? (JsonSerializer.Deserialize<ToolProjectHistoryItem[]>(await File.ReadAllTextAsync(HistoryPath), JsonOptions) ?? []).ToList()
                : [];
            if (!hasSavedHistory && Directory.Exists(DefaultProjectsRoot))
            {
                foreach (var directory in Directory.EnumerateDirectories(DefaultProjectsRoot))
                {
                    if (!File.Exists(Path.Combine(directory, "manifest.json"))
                        || items.Any(item => item.ProjectPath.Equals(directory, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    items.Add(new ToolProjectHistoryItem(
                        await ReadProjectNameAsync(directory),
                        Path.GetFullPath(directory),
                        Directory.GetLastWriteTimeUtc(directory)));
                }
            }
            return items.OrderByDescending(item => item.LastOpenedUtc).Take(30).ToArray();
        }
        catch
        {
            return [];
        }
    }

    public static async Task RememberProjectAsync(string projectPath)
    {
        var fullPath = Path.GetFullPath(projectPath);
        var items = (await LoadHistoryAsync()).ToList();
        items.RemoveAll(item => item.ProjectPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        items.Insert(0, new ToolProjectHistoryItem(await ReadProjectNameAsync(fullPath), fullPath, DateTime.UtcNow));
        await SaveHistoryAsync(items.Take(30));
    }

    public static async Task RemoveProjectFromHistoryAsync(string projectPath)
    {
        var items = (await LoadHistoryAsync())
            .Where(item => !item.ProjectPath.Equals(projectPath, StringComparison.OrdinalIgnoreCase));
        await SaveHistoryAsync(items);
    }

    public static async Task<IReadOnlyList<string>> LoadExplorerOrderAsync(string projectPath)
    {
        try
        {
            var path = GetLayoutPath(projectPath);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(path), JsonOptions) ?? []
                : [];
        }
        catch
        {
            return [];
        }
    }

    public static async Task SaveExplorerOrderAsync(string projectPath, IEnumerable<string> relativePaths)
    {
        var path = GetLayoutPath(projectPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(relativePaths.Distinct(StringComparer.OrdinalIgnoreCase), JsonOptions), new UTF8Encoding(false));
    }

    private static async Task<string> ReadProjectNameAsync(string root)
    {
        try
        {
            var manifestPath = Path.Combine(root, "manifest.json");
            var manifest = JsonSerializer.Deserialize<ToolPackageManifest>(await File.ReadAllTextAsync(manifestPath), JsonOptions);
            if (!string.IsNullOrWhiteSpace(manifest?.Name))
                return manifest.Name;
        }
        catch
        {
            // 清单缺失或格式错误时使用目录名作为历史项目名称。
        }

        return Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
    }

    private static async Task SaveHistoryAsync(IEnumerable<ToolProjectHistoryItem> items)
    {
        Directory.CreateDirectory(DefaultProjectsRoot);
        await File.WriteAllTextAsync(HistoryPath, JsonSerializer.Serialize(items, JsonOptions), new UTF8Encoding(false));
    }

    private static async Task WriteDefaultToolIconAsync(string outputPath)
    {
        var assemblyName = typeof(ToolProjectWorkspaceService).Assembly.GetName().Name
                           ?? throw new InvalidOperationException("无法确定客户端程序集名称。");
        var resourceUri = new Uri(
            $"pack://application:,,,/{assemblyName};component/{DefaultToolIconResourcePath}",
            UriKind.Absolute);
        var resource = Application.GetResourceStream(resourceUri)
                       ?? throw new InvalidOperationException("无法读取内置的默认工具图标。");

        await using var source = resource.Stream;
        await using var destination = new FileStream(
            outputPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous);
        await source.CopyToAsync(destination);
    }

    private static string GetLayoutPath(string projectPath)
    {
        var normalized = Path.GetFullPath(projectPath).ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return Path.Combine(AppPath.AppLocalData, "EditorLayouts", key + ".json");
    }
}

internal sealed record ToolProjectHistoryItem(string Name, string ProjectPath, DateTime LastOpenedUtc)
{
    [JsonIgnore]
    public bool Exists => Directory.Exists(ProjectPath);
    [JsonIgnore]
    public string StateText => Exists ? "可用" : "位置不存在";
    [JsonIgnore]
    public string LastOpenedText => LastOpenedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}
