using System.Text.Json;
using System.Windows;
using XFEToolBox.Client.Models;
using XFEToolBox.Client.Profiles.CacheProfiles;
using XFEToolBox.Client.Views.Pages;
using XFEToolBox.Core.Downloads;
using XFEToolBox.Core.Tools;

namespace XFEToolBox.Client.Utilities;

public static class LauncherService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<IReadOnlyList<LauncherItem>> SearchAsync(string? query, int maximumCount = 20)
    {
        var items = await GetAllItemsAsync();
        var pinnedOrder = PinnedItemService.GetPinnedItems()
            .Select((entry, index) => (PinnedItemService.CreateKey(entry.Kind, entry.TargetId), index))
            .ToDictionary(item => item.Item1, item => item.index, StringComparer.OrdinalIgnoreCase);
        query = query?.Trim() ?? string.Empty;

        if (query.Length == 0)
        {
            return items
                .OrderBy(item => pinnedOrder.TryGetValue(item.Key, out var order) ? order : int.MaxValue)
                .ThenByDescending(item => item.LastUsedAtUtc)
                .ThenBy(item => item.Kind is LauncherItemKind.Command ? 0 : 1)
                .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
                .Take(maximumCount)
                .ToArray();
        }

        return items
            .Select(item => (Item: item, Score: GetMatchScore(item, query)))
            .Where(result => result.Score < int.MaxValue)
            .OrderBy(result => result.Score)
            .ThenBy(result => pinnedOrder.TryGetValue(result.Item.Key, out var order) ? order : int.MaxValue)
            .ThenByDescending(result => result.Item.LastUsedAtUtc)
            .ThenBy(result => result.Item.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(maximumCount)
            .Select(result => result.Item)
            .ToArray();
    }

    public static async Task<IReadOnlyList<LauncherItem>> GetQuickAccessAsync()
    {
        var allItems = await GetAllItemsAsync();
        var index = allItems
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var result = new List<LauncherItem>(PinnedItemService.MaximumPinnedItems);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pinned in PinnedItemService.GetPinnedItems())
        {
            var key = PinnedItemService.CreateKey(pinned.Kind, pinned.TargetId);
            if (index.TryGetValue(key, out var item) && keys.Add(key)) result.Add(item);
        }

        foreach (var item in allItems.Where(item => item.LastUsedAtUtc.HasValue).OrderByDescending(item => item.LastUsedAtUtc))
            if (keys.Add(item.Key)) result.Add(item);

        foreach (var defaultKey in new[] { "Page:tool", "Page:console", "Command:new-tool", "Page:download" })
            if (index.TryGetValue(defaultKey, out var item) && keys.Add(item.Key)) result.Add(item);

        return result.Take(8).ToArray();
    }

    public static async Task<IReadOnlyList<LauncherItem>> GetRecentlyUpdatedToolsAsync(int maximumCount = 4)
    {
        var tools = ReadToolCatalog()
            .OrderByDescending(tool => tool.UpdatedAtUtc)
            .Take(maximumCount)
            .ToArray();
        var recent = GetRecentIndex();
        return tools.Select(tool => CreateToolItem(tool, recent)).ToArray();
    }

    public static async Task<IReadOnlyList<LauncherItem>> GetAllItemsAsync()
    {
        var recent = GetRecentIndex();
        var items = new List<LauncherItem>();
        items.AddRange(CreatePageItems(recent));
        items.AddRange(CreateCommandItems(recent));
        items.AddRange(ReadToolCatalog().Select(tool => CreateToolItem(tool, recent)));
        items.AddRange(ReadSoftwareCatalog().Select(software => CreateSoftwareItem(software, recent)));

        foreach (var project in await ToolProjectWorkspaceService.LoadHistoryAsync())
        {
            if (!project.Exists) continue;
            var key = PinnedItemService.CreateKey(LauncherItemKind.Project, project.ProjectPath);
            items.Add(new LauncherItem
            {
                Kind = LauncherItemKind.Project,
                TargetId = project.ProjectPath,
                Title = project.Name,
                Subtitle = "Code Studio 项目",
                Detail = project.ProjectPath,
                Keywords = ["项目", "代码", "Code Studio", project.ProjectPath],
                IconReference = "/Resources/Image/wrench.png",
                IsPinned = PinnedItemService.IsPinned(LauncherItemKind.Project, project.ProjectPath),
                LastUsedAtUtc = recent.TryGetValue(key, out var entry) ? entry.LastUsedAtUtc : project.LastOpenedUtc,
                ExecuteAsync = () => ToolWorkshopService.OpenProjectAsync(project.ProjectPath)
            });
        }

        return items;
    }

    public static int GetMatchScore(LauncherItem item, string query)
    {
        if (string.Equals(item.Title, query, StringComparison.CurrentCultureIgnoreCase)) return AdjustScore(item, 0);
        if (item.Title.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)) return AdjustScore(item, 100);
        if (item.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)) return AdjustScore(item, 200);
        if (item.Subtitle.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            item.Detail.Contains(query, StringComparison.CurrentCultureIgnoreCase)) return AdjustScore(item, 300);
        if (item.Keywords.Any(keyword => keyword.Contains(query, StringComparison.CurrentCultureIgnoreCase))) return AdjustScore(item, 400);
        return int.MaxValue;
    }

    private static int AdjustScore(LauncherItem item, int score)
    {
        if (item.IsPinned) score -= 20;
        if (item.LastUsedAtUtc.HasValue) score -= 10;
        return score;
    }

    private static IEnumerable<LauncherItem> CreatePageItems(IReadOnlyDictionary<string, RecentUsageEntry> recent)
    {
        var definitions = new[]
        {
            (Id: "home", Title: "首页", Subtitle: "个人工作台", Icon: "/Resources/Image/grid.png", Keywords: new[] { "主页", "工作台" }),
            (Id: "chat", Title: "聊天", Subtitle: "好友、群聊与消息", Icon: "/Resources/Image/grid.png", Keywords: new[] { "好友", "群聊", "消息", "通话" }),
            (Id: "tool", Title: "工具箱", Subtitle: "浏览和运行工具", Icon: "/Resources/Image/toolbox.png", Keywords: new[] { "工具", "扩展" }),
            (Id: "workshop", Title: "工具工坊", Subtitle: "创建和编辑 WPF 工具", Icon: "/Resources/Image/wrench.png", Keywords: new[] { "Code Studio", "创作", "项目" }),
            (Id: "console", Title: "C# 控制台", Subtitle: "运行代码片段", Icon: "/Resources/Image/console.png", Keywords: new[] { "代码", "调试" }),
            (Id: "download", Title: "下载专区", Subtitle: "获取开发软件", Icon: "/Resources/Image/download.png", Keywords: new[] { "软件", "下载" }),
            (Id: "setting", Title: "选项设置", Subtitle: "配置工具箱", Icon: "/Resources/Image/setting.png", Keywords: new[] { "配置", "快捷键" })
        };
        foreach (var definition in definitions)
        {
            var key = PinnedItemService.CreateKey(LauncherItemKind.Page, definition.Id);
            yield return new LauncherItem
            {
                Kind = LauncherItemKind.Page,
                TargetId = definition.Id,
                Title = definition.Title,
                Subtitle = definition.Subtitle,
                Keywords = definition.Keywords,
                IconReference = definition.Icon,
                IsPinned = PinnedItemService.IsPinned(LauncherItemKind.Page, definition.Id),
                LastUsedAtUtc = recent.TryGetValue(key, out var entry) ? entry.LastUsedAtUtc : null,
                ExecuteAsync = () =>
                {
                    ((App)Application.Current).ShowMainWindow(definition.Id);
                    return Task.CompletedTask;
                }
            };
        }
    }

    private static IEnumerable<LauncherItem> CreateCommandItems(IReadOnlyDictionary<string, RecentUsageEntry> recent)
    {
        yield return CreateCommand("new-tool", "新建工具", "创建 Code Studio 工具项目", ["创建", "项目", "Code Studio"],
            () => ToolWorkshopService.ShowNewProjectDialog());
        yield return CreateCommand("continue-project", "继续上次项目", "打开最近的 Code Studio 工具项目", ["继续", "项目", "编辑"],
            () => _ = ToolWorkshopService.ContinueLastProjectAsync());
        yield return CreateCommand("open-projects", "打开工具项目", "浏览本地与历史工具项目", ["项目", "历史", "浏览"],
            () => ToolWorkshopService.ShowProjectLauncher());
    }

    private static LauncherItem CreateCommand(string id, string title, string subtitle, string[] keywords, Action action) => new()
    {
        Kind = LauncherItemKind.Command,
        TargetId = id,
        Title = title,
        Subtitle = subtitle,
        Keywords = keywords,
        IconReference = "/Resources/Image/wrench.png",
        IsPinned = PinnedItemService.IsPinned(LauncherItemKind.Command, id),
        ExecuteAsync = () =>
        {
            action();
            return Task.CompletedTask;
        }
    };

    private static LauncherItem CreateToolItem(ToolPackageSummary tool, IReadOnlyDictionary<string, RecentUsageEntry> recent)
    {
        var key = PinnedItemService.CreateKey(LauncherItemKind.Tool, tool.Id);
        return new LauncherItem
        {
            Kind = LauncherItemKind.Tool,
            TargetId = tool.Id,
            Title = tool.Name,
            Subtitle = $"{tool.Category} · {tool.Author}",
            Detail = tool.Description,
            Keywords = tool.Tags ?? [],
            IconReference = tool.IconDataUrl ?? string.Empty,
            IsPinned = PinnedItemService.IsPinned(LauncherItemKind.Tool, tool.Id),
            LastUsedAtUtc = recent.TryGetValue(key, out var entry) ? entry.LastUsedAtUtc : null,
            ExecuteAsync = async () =>
            {
                ((App)Application.Current).ShowMainWindow("tool");
                await ToolBoxPage.Current.OpenToolByIdAsync(tool.Id);
            }
        };
    }

    private static LauncherItem CreateSoftwareItem(SoftwareCatalogItem software, IReadOnlyDictionary<string, RecentUsageEntry> recent)
    {
        var key = PinnedItemService.CreateKey(LauncherItemKind.Software, software.Id);
        return new LauncherItem
        {
            Kind = LauncherItemKind.Software,
            TargetId = software.Id,
            Title = software.Name,
            Subtitle = $"{software.Category} · {software.Publisher}",
            Detail = software.Summary,
            Keywords = software.Tags ?? [],
            IconReference = software.IconUrl,
            IsPinned = PinnedItemService.IsPinned(LauncherItemKind.Software, software.Id),
            LastUsedAtUtc = recent.TryGetValue(key, out var entry) ? entry.LastUsedAtUtc : null,
            ExecuteAsync = async () =>
            {
                ((App)Application.Current).ShowMainWindow("download");
                await DownloadPage.Current.OpenSoftwareByIdAsync(software.Id);
            }
        };
    }

    private static ToolPackageSummary[] ReadToolCatalog()
    {
        try
        {
            return string.IsNullOrWhiteSpace(AppCacheProfile.ToolCatalogJson)
                ? []
                : JsonSerializer.Deserialize<ToolPackageSummary[]>(AppCacheProfile.ToolCatalogJson, JsonOptions) ?? [];
        }
        catch (JsonException) { return []; }
    }

    private static SoftwareCatalogItem[] ReadSoftwareCatalog()
    {
        try
        {
            return string.IsNullOrWhiteSpace(AppCacheProfile.SoftwareCatalogJson)
                ? []
                : (JsonSerializer.Deserialize<SoftwareCatalogResponse>(AppCacheProfile.SoftwareCatalogJson, JsonOptions)?.Items ?? []);
        }
        catch (JsonException) { return []; }
    }

    private static IReadOnlyDictionary<string, RecentUsageEntry> GetRecentIndex()
    {
        var result = new Dictionary<string, RecentUsageEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in RecentUsageService.GetRecent())
        {
            var kind = entry.Kind switch
            {
                RecentUsageKind.Tool => LauncherItemKind.Tool,
                RecentUsageKind.Software => LauncherItemKind.Software,
                RecentUsageKind.Project => LauncherItemKind.Project,
                _ => LauncherItemKind.Page
            };
            result[PinnedItemService.CreateKey(kind, entry.TargetId)] = entry;
        }
        return result;
    }
}
