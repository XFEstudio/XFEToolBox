using XFEToolBox.Client.Utilities;

namespace XFEToolBox.Client.Models;

public enum LauncherItemKind
{
    Page,
    Tool,
    Software,
    Project,
    Command
}

public sealed class PinnedItemEntry
{
    public LauncherItemKind Kind { get; set; }

    public string TargetId { get; set; } = string.Empty;

    public DateTimeOffset AddedAtUtc { get; set; }
}

public sealed class LauncherItem
{
    public required LauncherItemKind Kind { get; init; }

    public required string TargetId { get; init; }

    public required string Title { get; init; }

    public string Subtitle { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public string[] Keywords { get; init; } = [];

    public string IconReference { get; init; } = string.Empty;

    public bool IsPinned { get; init; }

    public DateTimeOffset? LastUsedAtUtc { get; init; }

    public required Func<Task> ExecuteAsync { get; init; }

    public string Key => PinnedItemService.CreateKey(Kind, TargetId);

    public string KindText => Kind switch
    {
        LauncherItemKind.Page => "页面",
        LauncherItemKind.Tool => "工具",
        LauncherItemKind.Software => "软件",
        LauncherItemKind.Project => "项目",
        _ => "命令"
    };
}
