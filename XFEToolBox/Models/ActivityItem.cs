using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XFEToolBox.Client.Models;

public enum ActivityKind
{
    General,
    SoftwareDownload,
    ToolPackage,
    Build,
    Publish,
    Update
}

public enum ActivityState
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

public sealed class ActivityItem : ObservableObject
{
    private readonly CancellationTokenSource? cancellationSource;
    private ActivityState state = ActivityState.Pending;
    private double? progress;
    private string message = string.Empty;
    private bool canCancel;

    internal ActivityItem(string id, ActivityKind kind, string title, bool canCancel)
    {
        Id = id;
        Kind = kind;
        Title = title;
        StartedAt = DateTimeOffset.Now;
        this.canCancel = canCancel;
        cancellationSource = canCancel ? new CancellationTokenSource() : null;
        CancelCommand = new RelayCommand(RequestCancel, () => CanCancel);
    }

    public string Id { get; }

    public ActivityKind Kind { get; }

    public string Title { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; internal set; }

    public ActivityState State
    {
        get => state;
        internal set
        {
            if (!SetProperty(ref state, value)) return;
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(CanCancel));
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    public double? Progress
    {
        get => progress;
        internal set
        {
            if (!SetProperty(ref progress, value is null ? null : Math.Clamp(value.Value, 0, 100))) return;
            OnPropertyChanged(nameof(ProgressText));
        }
    }

    public string Message
    {
        get => message;
        internal set => SetProperty(ref message, value);
    }

    public bool CanCancel
    {
        get => canCancel && State is ActivityState.Pending or ActivityState.Running;
        internal set
        {
            if (!SetProperty(ref canCancel, value)) return;
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    public string StateText => State switch
    {
        ActivityState.Pending => "等待中",
        ActivityState.Running => "进行中",
        ActivityState.Succeeded => "已完成",
        ActivityState.Failed => "失败",
        ActivityState.Cancelled => "已取消",
        _ => State.ToString()
    };

    public string ProgressText => Progress is { } value ? $"{value:0}%" : StateText;

    public RelayCommand CancelCommand { get; }

    internal CancellationToken Token => cancellationSource?.Token ?? CancellationToken.None;

    private void RequestCancel()
    {
        if (!CanCancel) return;
        Message = "正在取消…";
        cancellationSource?.Cancel();
        CanCancel = false;
    }
}
