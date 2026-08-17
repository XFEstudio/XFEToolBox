using System.Windows.Controls;

namespace XFEToolBox.WpfCore.Tutorial;

/// <summary>
/// 在任意 Panel 上启动统一的交互式教程。
/// </summary>
public interface ITutorialService
{
    bool IsRunning { get; }

    Task<TutorialResult> StartAsync(
        Panel host,
        IEnumerable<TutorialStep> steps,
        TutorialOptions? options = null,
        CancellationToken cancellationToken = default);

    void Cancel();
}
