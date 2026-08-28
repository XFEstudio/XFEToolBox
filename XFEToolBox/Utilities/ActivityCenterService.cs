using System.Collections.ObjectModel;
using System.Windows;
using XFEToolBox.Client.Models;

namespace XFEToolBox.Client.Utilities;

public static class ActivityCenterService
{
    private const int MaximumCompletedItems = 8;
    private static readonly ObservableCollection<ActivityItem> MutableItems = [];

    public static ReadOnlyObservableCollection<ActivityItem> Items { get; } = new(MutableItems);

    public static event EventHandler? Changed;

    public static int ActiveCount => MutableItems.Count(item => item.State is ActivityState.Pending or ActivityState.Running);

    public static ActivityHandle Start(string title, ActivityKind kind = ActivityKind.General, bool canCancel = false)
    {
        var item = new ActivityItem(Guid.NewGuid().ToString("N"), kind, title, canCancel)
        {
            State = ActivityState.Running
        };
        Dispatch(() =>
        {
            MutableItems.Insert(0, item);
            TrimCompletedItems();
            Changed?.Invoke(null, EventArgs.Empty);
        });
        return new ActivityHandle(item);
    }

    public static IReadOnlyList<ActivityItem> GetSnapshot(int maximumCount = 8) =>
        MutableItems.Take(Math.Max(0, maximumCount)).ToArray();

    public static string DescribeActiveItems() => string.Join(",\n", MutableItems
        .Where(item => item.State is ActivityState.Pending or ActivityState.Running)
        .Select(item => $"{item.Title} · {item.ProgressText}"));

    public static void ClearCompleted()
    {
        Dispatch(() =>
        {
            for (var index = MutableItems.Count - 1; index >= 0; index--)
                if (MutableItems[index].State is not (ActivityState.Pending or ActivityState.Running))
                    MutableItems.RemoveAt(index);
            Changed?.Invoke(null, EventArgs.Empty);
        });
    }

    internal static void NotifyChanged() => Dispatch(() =>
    {
        TrimCompletedItems();
        Changed?.Invoke(null, EventArgs.Empty);
    });

    internal static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }

    private static void TrimCompletedItems()
    {
        var completed = 0;
        for (var index = 0; index < MutableItems.Count; index++)
        {
            if (MutableItems[index].State is ActivityState.Pending or ActivityState.Running) continue;
            completed++;
            if (completed <= MaximumCompletedItems) continue;
            MutableItems.RemoveAt(index--);
        }
    }
}

public sealed class ActivityHandle : IDisposable
{
    private readonly ActivityItem item;
    private bool completed;

    internal ActivityHandle(ActivityItem item) => this.item = item;

    public ActivityItem Item => item;

    public CancellationToken CancellationToken => item.Token;

    public void Report(double? progress, string? message = null) => ActivityCenterService.Dispatch(() =>
    {
        if (completed) return;
        item.Progress = progress;
        if (message is not null) item.Message = message;
        ActivityCenterService.NotifyChanged();
    });

    public void Succeed(string? message = null) => Finish(ActivityState.Succeeded, message);

    public void Fail(string message) => Finish(ActivityState.Failed, message);

    public void Cancel(string? message = null) => Finish(ActivityState.Cancelled, message ?? "任务已取消");

    public void Dispose()
    {
        if (!completed) Succeed();
    }

    private void Finish(ActivityState state, string? message)
    {
        ActivityCenterService.Dispatch(() =>
        {
            if (completed) return;
            completed = true;
            item.State = state;
            item.Progress = state == ActivityState.Succeeded ? 100 : item.Progress;
            if (message is not null) item.Message = message;
            item.CompletedAt = DateTimeOffset.Now;
            item.CanCancel = false;
            ActivityCenterService.NotifyChanged();
        });
    }
}
