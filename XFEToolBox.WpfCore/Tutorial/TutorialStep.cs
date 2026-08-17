using System.Windows;

namespace XFEToolBox.WpfCore.Tutorial;

/// <summary>
/// 教程提示相对于高亮目标的首选位置。
/// </summary>
public enum TutorialPlacement
{
    Auto,
    Top,
    Right,
    Bottom,
    Left,
    Center
}

/// <summary>
/// 单个教程步骤。目标既可以在创建步骤时直接给出，也可以在显示步骤时延迟解析。
/// </summary>
public sealed class TutorialStep
{
    public required string Key { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public FrameworkElement? Target { get; init; }

    public Func<FrameworkElement?>? TargetResolver { get; init; }

    public TutorialPlacement Placement { get; init; } = TutorialPlacement.Auto;

    public Thickness SpotlightPadding { get; init; } = new(8);

    public double SpotlightCornerRadius { get; init; } = 14;

    /// <summary>
    /// 为 true 时，高亮区域中的鼠标事件会继续传递给真实控件。
    /// </summary>
    public bool AllowTargetInteraction { get; init; }

    public bool BringTargetIntoView { get; init; } = true;

    public string? Hint { get; init; }

    public string? NextButtonText { get; init; }

    /// <summary>
    /// 在步骤定位之前执行，可用于切换页面、展开面板或异步准备数据。
    /// </summary>
    public Func<CancellationToken, Task>? EnterAsync { get; init; }

    /// <summary>
    /// 离开当前步骤时执行。
    /// </summary>
    public Func<CancellationToken, Task>? LeaveAsync { get; init; }

    internal FrameworkElement? ResolveTarget() => TargetResolver?.Invoke() ?? Target;
}

public enum TutorialResult
{
    Completed,
    Skipped,
    Cancelled
}

/// <summary>
/// 一次教程会话的统一显示选项。
/// </summary>
public sealed class TutorialOptions
{
    public bool AllowSkip { get; init; } = true;

    public string SkipText { get; init; } = "跳过教程";

    public string PreviousText { get; init; } = "上一步";

    public string NextText { get; init; } = "下一步";

    public string FinishText { get; init; } = "开始使用";

    public double CalloutWidth { get; init; } = 350;

    public TimeSpan MotionDuration { get; init; } = TimeSpan.FromMilliseconds(320);
}
