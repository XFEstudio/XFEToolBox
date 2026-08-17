using System.Windows;
using System.Windows.Controls;
using XFEToolBox.WpfCore.Controls;

namespace XFEToolBox.WpfCore.Tutorial;

/// <summary>
/// 教程控件的生命周期管理器。调用方只负责提供挂载容器和步骤。
/// </summary>
public sealed class TutorialService : ITutorialService
{
    private TutorialOverlay? activeOverlay;

    public bool IsRunning => activeOverlay is not null;

    public async Task<TutorialResult> StartAsync(
        Panel host,
        IEnumerable<TutorialStep> steps,
        TutorialOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(steps);

        if (activeOverlay is not null)
            throw new InvalidOperationException("当前已有教程正在显示。");

        var stepList = steps.ToArray();
        if (stepList.Length == 0)
            return TutorialResult.Completed;

        var overlay = new TutorialOverlay
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        activeOverlay = overlay;
        Panel.SetZIndex(overlay, int.MaxValue);
        host.Children.Add(overlay);

        try
        {
            return await overlay.ShowAsync(stepList, options ?? new TutorialOptions(), cancellationToken);
        }
        finally
        {
            overlay.Close(TutorialResult.Cancelled);
            host.Children.Remove(overlay);
            if (ReferenceEquals(activeOverlay, overlay))
                activeOverlay = null;
        }
    }

    public void Cancel() => activeOverlay?.Close(TutorialResult.Cancelled);
}
