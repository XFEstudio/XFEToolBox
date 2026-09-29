using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using XFEToolBox.Tools.VideoStudio;
using XFEToolBox.WpfCore.Controls;
using XFEToolBox.WpfCore.Windowing;

namespace VideoStudio.Validation;
internal static partial class Program
{
    private static async Task InterfaceTests(string[] paths)
    {
        var trace = new BindingTrace(); PresentationTraceSources.DataBindingSource.Listeners.Add(trace); PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var page = new MainPage(); var vm = (MainPageViewModel)page.DataContext; vm.FfmpegPath = ffmpeg; vm.FfprobePath = ffprobe;
        var window = new Window { Content = page, Width = 1380, Height = 840, Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
        window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await vm.LoadSourceAsync(paths[0]); await vm.ImportAssetsAsync(paths.Skip(1));
        vm.AddVideoTrackCommand.Execute(null); vm.InsertAsset(vm.Assets[1], 1);
        vm.AddAudioTrackCommand.Execute(null); vm.InsertAsset(vm.Assets[2], 0);
        vm.SelectedTrack = vm.Tracks.First(t => t.Kind == MediaKind.Video && t.Index == 2); vm.InsertAsset(vm.Assets[3], 4);
        var menu = (Menu)page.FindName("EditorMenu");
        Check(menu.Items.Count == 6, "top menu bar exposes File/Edit/Track/Playback/View/Tools");
        foreach (MenuItem top in menu.Items)
        {
            top.IsSubmenuOpen = true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(top.Items.OfType<MenuItem>().All(m => m.Command is not null), "all " + top.Header + " menu items bind to real commands");
            top.IsSubmenuOpen = false;
        }
        vm.SelectedClip = vm.Clips.First(c => c.Kind == MediaKind.Audio); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(((StackPanel)page.FindName("AudioProperties")).IsVisible && !((StackPanel)page.FindName("VisualProperties")).IsVisible, "audio selection displays audio inspector only");
        var trackPicker = (ComboBox)page.FindName("PropertyTrack");
        Check(trackPicker.Items.Cast<TimelineTrack>().All(t => t.Kind == MediaKind.Audio) && (string?)trackPicker.SelectedValue == vm.SelectedClip.TrackId, "property track picker lists only audio tracks and preserves selection");
        trackPicker.IsDropDownOpen = true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); trackPicker.IsDropDownOpen = false;
        vm.VolumeText = "40"; vm.ApplyClipPropertiesCommand.Execute(null); Check(vm.SelectedClip.AudioVolume == 40, "WPF inspector can apply clip properties without read-only binding failures");
        vm.SelectedClip = vm.Clips.First(c => c.Kind == MediaKind.Video); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(!((StackPanel)page.FindName("AudioProperties")).IsVisible && ((StackPanel)page.FindName("VisualProperties")).IsVisible, "video selection switches to visual inspector");
        Check(trackPicker.Items.Cast<TimelineTrack>().All(t => t.Kind == MediaKind.Video) && (string?)trackPicker.SelectedValue == vm.SelectedClip.TrackId, "visual inspector tracks update without losing selected track");
        Check(Descendants<TextBlock>(trackPicker).Any(t => t.Text == "V1") && !Descendants<TextBlock>(trackPicker).Any(t => t.Text.Contains("XFEToolBox.Tools")), "track picker displays V1 name, not CLR type name");
        var timeline = (TimelineSurface)page.FindName("Timeline"); var video = vm.SelectedClip!;
        double y = timeline.YAt(vm.TrackFor(video)!) + 30, scale = timeline.XAt(1);
        var tail = new Point(timeline.XAt(video.TimelineEnd) - 4, y);
        Check(timeline.EdgeAt(tail) is { Edge: ClipEdge.End } hit && hit.Clip == video, "right handle hit testing identifies selected video track");
        timeline.BeginDrag(tail); var trimmed = new Point(tail.X - .4 * scale, tail.Y); timeline.MoveDrag(trimmed); timeline.FinishDrag(trimmed);
        Check(Near(video.EndSeconds, 3.6) && vm.Clips.First(c => c.AssetId == video.AssetId && c.Kind == MediaKind.Audio).EndSeconds == 4, "actual pointer gesture trims video independently of original audio"); vm.UndoCommand.Execute(null);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); video = vm.Clips.First(c => c.Id == video.Id); scale = timeline.XAt(1); y = timeline.YAt(vm.TrackFor(video)!) + 30;
        var head = new Point(timeline.XAt(video.TimelineStart) + 3, y); timeline.BeginDrag(head); timeline.MoveDrag(new Point(head.X + .5 * scale, y)); timeline.CancelDrag();
        Check(!vm.IsTrimming && video.StartSeconds == 0 && video.TimelineStart == 0, "pointer capture cancellation restores head trim");
        var body = new Point(timeline.XAt(1), y); var targetTrack = vm.Tracks.First(t => t.Kind == MediaKind.Video && t.Index == 2);
        timeline.BeginDrag(body); var end = new Point(body.X + 2 * scale, timeline.YAt(targetTrack) + 30); timeline.MoveDrag(end); timeline.FinishDrag(end);
        Check(video.TrackId == targetTrack.Id && Near(video.TimelineStart, 2), "body drag moves clip across compatible tracks"); vm.UndoCommand.Execute(null);
        vm.SeekSequence(1.5); await Task.Delay(1500); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(((Image)page.FindName("PausedFrame")).Source is not null && ((TextBlock)page.FindName("PreviewHint")).Visibility == Visibility.Collapsed, "WPF displays a real multi-track composite preview");
        var player = (MediaElement)page.FindName("PreviewPlayer"); player.IsMuted = true;
        vm.SeekSequence(0); vm.TogglePlaybackCommand.Execute(null);
        var playback = Stopwatch.StartNew(); while (playback.Elapsed < TimeSpan.FromSeconds(25) && vm.SequencePosition < .3) await Task.Delay(100);
        Check(vm.IsPlaying && vm.SequencePosition >= .3 && player.NaturalDuration.HasTimeSpan, "actual WPF composite playback opens and advances sequence playhead");
        vm.TogglePlaybackCommand.Execute(null); Check(!vm.IsPlaying, "transport command pauses actual playback"); vm.SeekSequence(1.5); await Task.Delay(600);
        Save(page, "multi-track-editor.png");
        vm.SelectedClip = vm.Clips.First(c => c.Kind == MediaKind.Audio); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Save(page, "audio-inspector.png");
        window.Width = 1040; window.Height = 760; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Save(page, "minimum-editor.png");
        Check(((FrameworkElement)page.FindName("EditPage")).ActualHeight > 500 && timeline.ActualHeight >= 4 * TimelineSurface.RowHeight, "minimum-width layout retains editor and scrollable independent tracks");
        Check(((ScrollViewer)page.FindName("TimelineScroll")).ScrollableWidth < 3, "fit timeline follows resized viewport instead of retaining stale width");
        vm.ShowSubtitlesCommand.Execute(null); vm.Subtitles.Add(new SubtitleCue { Index = 1, StartSeconds = 0, EndSeconds = 1, Text = "字幕回归检查" }); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Save(page, "subtitles.png");
        vm.ShowEnvironmentCommand.Execute(null); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Save(page, "sequence-settings.png");
        Check(trace.Errors.Count == 0, "zero WPF binding errors: " + string.Join(" | ", trace.Errors));
        vm.IsBusy = true; window.Close(); Check(window.IsVisible, "closing during active task requests cancellation and waits for cleanup"); vm.IsBusy = false; vm.HasUnsavedChanges = false; window.Close();
        PresentationTraceSources.DataBindingSource.Listeners.Remove(trace);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T value) yield return value; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorData { public int Size; public NativeRect Bounds, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hWnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hWnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorData info);
    private static async Task ChromeTests()
    {
        var content = new Border { CornerRadius = new CornerRadius(16), Background = Brushes.White };
        var surface = new RoundedClipBorder { Margin = new Thickness(5), CornerRadius = new CornerRadius(19), Background = Brushes.Purple, Child = content };
        var grip = new WindowResizeGrip(); var grid = new Grid(); grid.Children.Add(surface); grid.Children.Add(grip);
        var window = new Window { Content = grid, Width = 800, Height = 600, Left = 100, Top = 100, ShowActivated = false, ShowInTaskbar = false, Opacity = 0, AllowsTransparency = true, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.CanResize };
        ToolWindowFrameBehavior.Attach(window, surface, content); WindowWorkAreaHelper.Attach(window); window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(surface.Margin == new Thickness(5) && surface.CornerRadius.TopLeft == 19 && grip.IsVisible, "normal tool chrome retains rounded border and resize grip");
        window.WindowState = WindowState.Maximized; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(surface.Margin == new Thickness(0) && surface.CornerRadius == new CornerRadius(0) && content.CornerRadius == new CornerRadius(0), "maximized chrome removes all outer margins and both corner radii");
        Check(grip.Visibility == Visibility.Collapsed && !grip.IsHitTestVisible, "maximized resize grip is hidden and cannot intercept pointer input");
        nint hwnd = new WindowInteropHelper(window).Handle; GetWindowRect(hwnd, out var bounds); var monitor = new MonitorData { Size = Marshal.SizeOf<MonitorData>() }; GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitor);
        Check(bounds.Left == monitor.Work.Left && bounds.Top == monitor.Work.Top && bounds.Right == monitor.Work.Right && bounds.Bottom == monitor.Work.Bottom, "actual maximized Win32 bounds exactly fill monitor work area without covering taskbar");
        window.WindowState = WindowState.Normal; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(surface.Margin == new Thickness(5) && surface.CornerRadius.TopLeft == 19 && content.CornerRadius.TopLeft == 16 && grip.IsVisible, "restore reinstates original chrome and grip");
        window.ResizeMode = ResizeMode.NoResize; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Check(grip.Visibility == Visibility.Collapsed, "non-resizable tool also hides resize grip"); window.Close();
    }
}
