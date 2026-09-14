using System.Windows;
using System.Windows.Controls;
using XFEToolBox.Tools.SpaceEngineers.ViewModels;

namespace XFEToolBox.Tools.SpaceEngineers.Views;

public partial class MainPage : UserControl
{
    private readonly bool initialize;
    private MainPageViewModel? ViewModel => DataContext as MainPageViewModel;

    public MainPage() : this(new MainPageViewModel(), true) { }

    // A supplied view model can be rendered without starting discovery, timers or game operations.
    public MainPage(object viewModel, bool initialize = false)
    {
        InitializeComponent();
        DataContext = viewModel;
        this.initialize = initialize;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        LogBox.TextChanged += OnLogChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (initialize && ViewModel is { } vm) await vm.InitializeAsync();
    }

    private void OnLogChanged(object sender, TextChangedEventArgs e)
    {
        // Preserve the user's position when they have scrolled back through a long log.
        if (LogBox.VerticalOffset >= LogBox.ExtentHeight - LogBox.ViewportHeight - 40) LogBox.ScrollToEnd();
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        Loaded -= OnLoaded;
        LogBox.TextChanged -= OnLogChanged;
        if (initialize && ViewModel is { } vm) await vm.ShutdownAsync();
    }
}
