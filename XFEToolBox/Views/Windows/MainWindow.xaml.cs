using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Windows.Input;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.WpfCore.Tutorial;
using XFEToolBox.WpfCore.Windowing;
using MainWindowViewModel = XFEToolBox.Client.ViewModel.Windows.MainWindowViewModel;

namespace XFEToolBox.Client.Views.Windows;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public static MainWindow? Current { get; private set; }
    public MainWindowViewModel ViewModel { get; private set; }
    private readonly ITutorialService tutorialService = new TutorialService();
    private readonly CancellationTokenSource tutorialCancellationSource = new();
    private bool firstRunTutorialChecked;
    public MainWindow()
    {
        InitializeComponent();
        WindowWorkAreaHelper.Attach(this);
        ViewModel = new MainWindowViewModel(this);
        DataContext = ViewModel;
        Current = this;
        Width = SystemProfile.MainWindowWidth;
        Height = SystemProfile.MainWindowHeight;
        WindowState = SystemProfile.StartWithMaximize ? WindowState.Maximized : WindowState.Normal;
    }

    private void CaptionBar_CloseRequested(object? sender, EventArgs e) => MainWindowViewModel.CloseWindow();

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        mainButton.IsChecked = true;
        ViewModel.GetDPIScale();

        if (firstRunTutorialChecked)
            return;

        firstRunTutorialChecked = true;
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);

        if (!SystemProfile.MainTutorialCompleted)
        {
            var result = await StartMainTutorialAsync(tutorialCancellationSource.Token);
            if (result is TutorialResult.Completed or TutorialResult.Skipped)
                SystemProfile.MainTutorialCompleted = true;
        }

        if (SystemProfile.CheckForUpdatesOnStartup)
            _ = UpgradeService.CheckForUpdatesAsync(userInitiated: false, owner: this);
    }

    /// <summary>
    /// 启动主窗口教程。除首次自动调用外，设置页或帮助菜单也可以直接复用此入口。
    /// </summary>
    public Task<TutorialResult> StartMainTutorialAsync(CancellationToken cancellationToken = default) =>
        tutorialService.StartAsync(TutorialHost, CreateMainTutorialSteps(), new TutorialOptions
        {
            CalloutWidth = 350,
            MotionDuration = TimeSpan.FromMilliseconds(340),
            FinishText = "开始探索"
        }, cancellationToken);

    private IReadOnlyList<TutorialStep> CreateMainTutorialSteps()
    {
        var steps = new List<TutorialStep>
        {
            new()
            {
                Key = "welcome",
                Title = "欢迎使用 XFE·工具箱",
                Description = "用一分钟认识窗口操作和主要功能。教程会跟随目标平滑移动，窗口尺寸变化时也会自动重新定位。",
                Placement = TutorialPlacement.Center,
                SpotlightPadding = new Thickness(0),
                Hint = "可随时点击“跳过教程”；之后仍可通过 StartMainTutorialAsync 接口重新启动。"
            },
            new()
            {
                Key = "caption-bar",
                Title = "拖动、最大化与窗口控制",
                Description = "按住上方中间的短横线即可移动窗口；双击同一区域可最大化，再次双击可还原。右侧还可以最小化或关闭软件。",
                Target = CaptionBar.DragSurfaceElement,
                Placement = TutorialPlacement.Bottom,
                SpotlightPadding = new Thickness(5, 3, 5, 4),
                SpotlightCornerRadius = 12,
                AllowTargetInteraction = true,
                Hint = "高亮区域当前可直接操作，现在就可以拖动或双击试试看。"
            },
            new()
            {
                Key = "home",
                Title = "首页",
                Description = "通过统一搜索、快速访问、继续工作和活动中心，快速回到正在处理的内容。",
                Target = mainButton,
                Placement = TutorialPlacement.Right,
                SpotlightPadding = new Thickness(6),
                SpotlightCornerRadius = 17
            },
            new()
            {
                Key = "toolbox",
                Title = "工具箱",
                Description = "集中浏览本地与服务器工具。列表会先读取缓存立即显示，再在后台异步刷新。",
                Target = toolBoxButton,
                Placement = TutorialPlacement.Right,
                SpotlightPadding = new Thickness(6),
                SpotlightCornerRadius = 17
            },
            new()
            {
                Key = "console",
                Title = "C# 控制台",
                Description = "快速编写和运行 C# 代码片段，适合验证想法、调试表达式与处理临时代码。",
                Target = consoleButton,
                Placement = TutorialPlacement.Right,
                SpotlightPadding = new Thickness(6),
                SpotlightCornerRadius = 17
            },
            new()
            {
                Key = "workshop",
                Title = "工具工坊",
                Description = "所有用户都可以创建、预览、运行和导出本地 WPF 工具；服务器发布仍由管理员控制。",
                Target = workshopButton,
                Placement = TutorialPlacement.Right,
                SpotlightPadding = new Thickness(6),
                SpotlightCornerRadius = 17
            },
            new()
            {
                Key = "download",
                Title = "下载专区",
                Description = "查找常用开发软件并管理下载；支持多线程下载、进度控制和下载后操作。",
                Target = downloadButton,
                Placement = TutorialPlacement.Right,
                SpotlightPadding = new Thickness(6),
                SpotlightCornerRadius = 17
            },
            new()
            {
                Key = "settings",
                Title = "选项设置",
                Description = "调整启动方式、下载目录、并发数量等客户端行为，让工具箱更符合你的使用习惯。",
                Target = settingButton,
                Placement = TutorialPlacement.Right,
                SpotlightPadding = new Thickness(6),
                SpotlightCornerRadius = 17
            },
            new()
            {
                Key = "account",
                Title = "账户与个人中心",
                Description = "点击左下角账户卡片登录或注册；登录后可查看资料、管理账户并使用服务器相关能力。",
                Target = AccountEntry,
                Placement = TutorialPlacement.Right,
                SpotlightPadding = new Thickness(6),
                SpotlightCornerRadius = 17
            }
        };

        if (serverManagementButton.IsVisible)
        {
            steps.Add(new TutorialStep
            {
                Key = "server-management",
                Title = "服务器管理",
                Description = "管理员可以在这里查看服务器状态，并管理用户、工具与下载资源。普通账户不会显示此入口。",
                Target = serverManagementButton,
                Placement = TutorialPlacement.Right,
                SpotlightPadding = new Thickness(6),
                SpotlightCornerRadius = 17
            });
        }

        steps.Add(new TutorialStep
        {
            Key = "finish",
            Title = "准备就绪",
            Description = "现在可以从左侧选择任意功能开始使用。教程系统也可复用于任意页面、弹窗或新功能的分步说明。",
            Placement = TutorialPlacement.Center,
            SpotlightPadding = new Thickness(0),
            NextButtonText = "开始探索",
            Hint = "按 ← / → 切换步骤，按 Esc 可跳过教程。"
        });

        return steps;
    }

    private void ContentFrame_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
    {
        var storyboard = new Storyboard();
        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fadeIn, contentFrame);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));
        storyboard.Children.Add(fadeIn);
        storyboard.Begin();
    }

    private void WindowResizeGrip_ResizeCompleted(object? sender, EventArgs e)
    {
        SystemProfile.MainWindowWidth = Width;
        SystemProfile.MainWindowHeight = Height;
    }

    private void BackTabBorder_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => NavigationCenter.GoBack();

    private void AccountEntry_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ViewModel.NavigateToPageCommand.Execute("profile");
    }

    public void NavigateAndSelect(string pageTag)
    {
        ViewModel.NavigateToPageCommand.Execute(pageTag);
        mainButton.IsChecked = pageTag == "home";
        toolBoxButton.IsChecked = pageTag == "tool";
        workshopButton.IsChecked = pageTag == "workshop";
        consoleButton.IsChecked = pageTag == "console";
        downloadButton.IsChecked = pageTag == "download";
        settingButton.IsChecked = pageTag == "setting";
        serverManagementButton.IsChecked = pageTag is "serverManagement" or "serverOverview" or "userManagement" or "toolManagement" or "softwareManagement";
    }
}
