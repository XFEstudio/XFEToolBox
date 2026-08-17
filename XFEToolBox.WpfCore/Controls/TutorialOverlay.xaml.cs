using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using XFEToolBox.WpfCore.Tutorial;

namespace XFEToolBox.WpfCore.Controls;

/// <summary>
/// 带透明聚光区域和自动定位气泡的统一教程控件。
/// </summary>
public partial class TutorialOverlay : UserControl
{
    private IReadOnlyList<TutorialStep> steps = [];
    private TutorialOptions options = new();
    private TaskCompletionSource<TutorialResult>? sessionCompletion;
    private CancellationTokenRegistration cancellationRegistration;
    private CancellationToken sessionCancellationToken;
    private int currentIndex = -1;
    private bool isTransitioning;
    private Rect lastTargetRect = Rect.Empty;

    public TutorialOverlay()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
        SizeChanged += (_, _) => UpdateVisualPosition(false);
    }

    public bool IsRunning => sessionCompletion is { Task.IsCompleted: false };

    public async Task<TutorialResult> ShowAsync(
        IReadOnlyList<TutorialStep> tutorialSteps,
        TutorialOptions tutorialOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tutorialSteps);
        ArgumentNullException.ThrowIfNull(tutorialOptions);

        if (IsRunning)
            throw new InvalidOperationException("教程控件正在显示另一组步骤。");
        if (tutorialSteps.Count == 0)
            return TutorialResult.Completed;

        steps = tutorialSteps;
        options = tutorialOptions;
        sessionCancellationToken = cancellationToken;
        sessionCompletion = new TaskCompletionSource<TutorialResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        currentIndex = -1;
        lastTargetRect = Rect.Empty;
        Visibility = Visibility.Visible;

        await WaitForLoadedAsync(cancellationToken);
        if (cancellationToken.IsCancellationRequested)
            return TutorialResult.Cancelled;

        CalloutCard.Width = Math.Min(options.CalloutWidth, Math.Max(280, ActualWidth - 32));
        SkipButton.Content = options.SkipText;
        PreviousButton.Content = options.PreviousText;
        SkipButton.Visibility = options.AllowSkip ? Visibility.Visible : Visibility.Collapsed;

        cancellationRegistration = cancellationToken.Register(() =>
            Dispatcher.BeginInvoke(() => Close(TutorialResult.Cancelled)));
        CompositionTarget.Rendering += CompositionTarget_Rendering;

        await MoveToStepAsync(0);
        Focus();
        return await sessionCompletion.Task;
    }

    public void Close(TutorialResult result)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Close(result));
            return;
        }

        if (sessionCompletion is not { Task.IsCompleted: false } completion)
            return;

        CompositionTarget.Rendering -= CompositionTarget_Rendering;
        cancellationRegistration.Dispose();
        Visibility = Visibility.Collapsed;
        completion.TrySetResult(result);
    }

    private async Task MoveToStepAsync(int index)
    {
        if (isTransitioning || index < 0 || index >= steps.Count || sessionCompletion?.Task.IsCompleted != false)
            return;

        isTransitioning = true;
        SetNavigationEnabled(false);

        try
        {
            if (currentIndex >= 0 && steps[currentIndex].LeaveAsync is { } leaveAsync)
                await leaveAsync(sessionCancellationToken);

            currentIndex = index;
            var step = steps[currentIndex];
            if (step.EnterAsync is { } enterAsync)
                await enterAsync(sessionCancellationToken);

            var target = step.ResolveTarget();
            if (step.BringTargetIntoView && target is { IsVisible: true })
                target.BringIntoView();

            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            UpdateCardContent(step);
            UpdateVisualPosition(true);
        }
        catch (OperationCanceledException) when (sessionCancellationToken.IsCancellationRequested)
        {
            Close(TutorialResult.Cancelled);
        }
        catch (Exception exception)
        {
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            cancellationRegistration.Dispose();
            Visibility = Visibility.Collapsed;
            sessionCompletion?.TrySetException(exception);
        }
        finally
        {
            isTransitioning = false;
            SetNavigationEnabled(true);
        }
    }

    private void UpdateCardContent(TutorialStep step)
    {
        TitleText.Text = step.Title;
        DescriptionText.Text = step.Description;
        StepCounterText.Text = $"{currentIndex + 1:00} / {steps.Count:00}";
        HintText.Text = step.Hint ?? string.Empty;
        HintPanel.Visibility = string.IsNullOrWhiteSpace(step.Hint) ? Visibility.Collapsed : Visibility.Visible;
        PreviousButton.Visibility = currentIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = step.NextButtonText ??
                             (currentIndex == steps.Count - 1 ? options.FinishText : options.NextText);

        var progress = (double)(currentIndex + 1) / steps.Count;
        ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation
        {
            To = progress,
            Duration = new Duration(options.MotionDuration),
            EasingFunction = CreateEase()
        });

        CalloutCard.Measure(new Size(CalloutCard.Width, Math.Max(0, ActualHeight - 24)));
        CalloutCard.Opacity = 0;
        CalloutScale.ScaleX = CalloutScale.ScaleY = 0.96;
        CalloutTranslate.Y = 9;
        CalloutCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(options.MotionDuration)));
        CalloutScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, new Duration(options.MotionDuration)) { EasingFunction = CreateEase() });
        CalloutScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, new Duration(options.MotionDuration)) { EasingFunction = CreateEase() });
        CalloutTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(9, 0, new Duration(options.MotionDuration)) { EasingFunction = CreateEase() });
    }

    private void UpdateVisualPosition(bool animate)
    {
        if (currentIndex < 0 || currentIndex >= steps.Count || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        OuterGeometry.Rect = new Rect(0, 0, ActualWidth, ActualHeight);
        var step = steps[currentIndex];
        var targetRect = GetTargetRect(step);
        ApplySpotlight(step, targetRect, animate);
        PositionCallout(step, targetRect, animate);
        lastTargetRect = targetRect;
    }

    private Rect GetTargetRect(TutorialStep step)
    {
        var target = step.ResolveTarget();
        if (target is not { IsVisible: true } || target.ActualWidth <= 0 || target.ActualHeight <= 0)
            return Rect.Empty;

        try
        {
            var transform = target.TransformToVisual(this);
            var bounds = transform.TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
            var padding = step.SpotlightPadding;
            var left = Math.Clamp(bounds.Left - padding.Left, 4, Math.Max(4, ActualWidth - 4));
            var top = Math.Clamp(bounds.Top - padding.Top, 4, Math.Max(4, ActualHeight - 4));
            var right = Math.Clamp(bounds.Right + padding.Right, left, Math.Max(left, ActualWidth - 4));
            var bottom = Math.Clamp(bounds.Bottom + padding.Bottom, top, Math.Max(top, ActualHeight - 4));
            return new Rect(left, top, right - left, bottom - top);
        }
        catch (InvalidOperationException)
        {
            return Rect.Empty;
        }
    }

    private void ApplySpotlight(TutorialStep step, Rect targetRect, bool animate)
    {
        if (targetRect.IsEmpty || targetRect.Width <= 0 || targetRect.Height <= 0)
        {
            SpotlightGeometry.BeginAnimation(RectangleGeometry.RectProperty, null);
            SpotlightGeometry.Rect = Rect.Empty;
            SpotlightBorder.Visibility = Visibility.Collapsed;
            TargetBlocker.Visibility = Visibility.Collapsed;
            return;
        }

        SpotlightGeometry.RadiusX = SpotlightGeometry.RadiusY = step.SpotlightCornerRadius;
        SpotlightBorder.CornerRadius = new CornerRadius(step.SpotlightCornerRadius);
        SpotlightBorder.Visibility = Visibility.Visible;
        TargetBlocker.Visibility = step.AllowTargetInteraction ? Visibility.Collapsed : Visibility.Visible;

        var from = SpotlightGeometry.Rect;
        if (from.IsEmpty || from.Width <= 0 || from.Height <= 0)
            from = new Rect(targetRect.X + targetRect.Width / 2, targetRect.Y + targetRect.Height / 2, 1, 1);

        SpotlightGeometry.BeginAnimation(RectangleGeometry.RectProperty, null);
        SpotlightGeometry.Rect = targetRect;
        if (animate)
        {
            SpotlightGeometry.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation(from, targetRect, new Duration(options.MotionDuration))
            {
                EasingFunction = CreateEase()
            });
        }

        SetElementRect(SpotlightBorder, targetRect, animate);
        SetElementRect(TargetBlocker, targetRect, animate);
    }

    private void PositionCallout(TutorialStep step, Rect targetRect, bool animate)
    {
        var cardWidth = Math.Min(options.CalloutWidth, Math.Max(280, ActualWidth - 32));
        CalloutCard.Width = cardWidth;
        CalloutCard.Measure(new Size(cardWidth, Math.Max(0, ActualHeight - 24)));
        var cardHeight = Math.Min(CalloutCard.DesiredSize.Height, Math.Max(0, ActualHeight - 24));
        var position = CalculateCalloutPosition(step.Placement, targetRect, cardWidth, cardHeight);

        SetAnimatedValue(CalloutCard, Canvas.LeftProperty, position.X, animate);
        SetAnimatedValue(CalloutCard, Canvas.TopProperty, position.Y, animate);
    }

    private Point CalculateCalloutPosition(TutorialPlacement placement, Rect targetRect, double width, double height)
    {
        const double margin = 16;
        const double gap = 18;

        if (targetRect.IsEmpty || placement == TutorialPlacement.Center)
            return new Point(Math.Max(margin, (ActualWidth - width) / 2), Math.Max(margin, (ActualHeight - height) / 2));

        if (placement == TutorialPlacement.Auto)
        {
            var candidates = new[]
            {
                (Placement: TutorialPlacement.Right, Space: ActualWidth - targetRect.Right - gap, Fits: ActualWidth - targetRect.Right - gap >= width),
                (Placement: TutorialPlacement.Bottom, Space: ActualHeight - targetRect.Bottom - gap, Fits: ActualHeight - targetRect.Bottom - gap >= height),
                (Placement: TutorialPlacement.Left, Space: targetRect.Left - gap, Fits: targetRect.Left - gap >= width),
                (Placement: TutorialPlacement.Top, Space: targetRect.Top - gap, Fits: targetRect.Top - gap >= height)
            };
            placement = candidates.Where(candidate => candidate.Fits)
                                  .OrderByDescending(candidate => candidate.Space)
                                  .Select(candidate => candidate.Placement)
                                  .FirstOrDefault();
            if (placement == TutorialPlacement.Auto)
                placement = candidates.OrderByDescending(candidate => candidate.Space).First().Placement;
        }

        var left = placement switch
        {
            TutorialPlacement.Left => targetRect.Left - width - gap,
            TutorialPlacement.Right => targetRect.Right + gap,
            _ => targetRect.Left + (targetRect.Width - width) / 2
        };
        var top = placement switch
        {
            TutorialPlacement.Top => targetRect.Top - height - gap,
            TutorialPlacement.Bottom => targetRect.Bottom + gap,
            _ => targetRect.Top + (targetRect.Height - height) / 2
        };

        return new Point(
            Math.Clamp(left, margin, Math.Max(margin, ActualWidth - width - margin)),
            Math.Clamp(top, margin, Math.Max(margin, ActualHeight - height - margin)));
    }

    private void SetElementRect(FrameworkElement element, Rect rect, bool animate)
    {
        SetAnimatedValue(element, Canvas.LeftProperty, rect.Left, animate);
        SetAnimatedValue(element, Canvas.TopProperty, rect.Top, animate);
        SetAnimatedValue(element, WidthProperty, rect.Width, animate);
        SetAnimatedValue(element, HeightProperty, rect.Height, animate);
    }

    private void SetAnimatedValue(FrameworkElement element, DependencyProperty property, double value, bool animate)
    {
        var current = (double)element.GetValue(property);
        if (double.IsNaN(current) || double.IsInfinity(current))
            current = value;

        element.BeginAnimation(property, null);
        element.SetValue(property, value);
        if (animate && Math.Abs(current - value) > 0.25)
        {
            element.BeginAnimation(property, new DoubleAnimation(current, value, new Duration(options.MotionDuration))
            {
                EasingFunction = CreateEase()
            });
        }
    }

    private void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        if (currentIndex < 0 || isTransitioning || sessionCompletion?.Task.IsCompleted != false)
            return;

        var currentRect = GetTargetRect(steps[currentIndex]);
        if (!AreClose(currentRect, lastTargetRect))
            UpdateVisualPosition(false);
    }

    private static bool AreClose(Rect left, Rect right)
    {
        if (left.IsEmpty || right.IsEmpty)
            return left.IsEmpty && right.IsEmpty;
        return Math.Abs(left.X - right.X) < 0.5 && Math.Abs(left.Y - right.Y) < 0.5 &&
               Math.Abs(left.Width - right.Width) < 0.5 && Math.Abs(left.Height - right.Height) < 0.5;
    }

    private static CubicEase CreateEase() => new() { EasingMode = EasingMode.EaseOut };

    private void SetNavigationEnabled(bool enabled)
    {
        PreviousButton.IsEnabled = enabled;
        NextButton.IsEnabled = enabled;
        SkipButton.IsEnabled = enabled;
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e) => await MoveToStepAsync(currentIndex - 1);

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentIndex < steps.Count - 1)
        {
            await MoveToStepAsync(currentIndex + 1);
            return;
        }

        if (steps[currentIndex].LeaveAsync is { } leaveAsync)
            await leaveAsync(sessionCancellationToken);
        Close(TutorialResult.Completed);
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e) => Close(TutorialResult.Skipped);

    private async void TutorialOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && options.AllowSkip)
        {
            e.Handled = true;
            Close(TutorialResult.Skipped);
        }
        else if (e.Key == Key.Left && currentIndex > 0)
        {
            e.Handled = true;
            await MoveToStepAsync(currentIndex - 1);
        }
        else if (e.Key is Key.Right or Key.Enter)
        {
            e.Handled = true;
            NextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }

    private Task WaitForLoadedAsync(CancellationToken cancellationToken)
    {
        if (IsLoaded)
            return Task.CompletedTask;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler? handler = null;
        handler = (_, _) =>
        {
            Loaded -= handler;
            completion.TrySetResult();
        };
        Loaded += handler;
        cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return completion.Task;
    }
}
