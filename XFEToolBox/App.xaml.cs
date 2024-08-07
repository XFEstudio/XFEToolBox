using System.Windows;

namespace XFEToolBox;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        this.InitializeComponent();
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 注册 UI 线程未处理异常事件
        this.DispatcherUnhandledException += new System.Windows.Threading.DispatcherUnhandledExceptionEventHandler(App_DispatcherUnhandledException);

        // 注册非 UI 线程未处理异常事件
        AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);

        // 注册任务调度程序未观察到的任务异常事件
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // 处理 UI 线程未处理的异常
        MessageBox.Show("UI线程未处理异常：" + e.Exception.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // 设置为 true 表示异常已处理，防止应用程序退出
    }

    void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // 处理非 UI 线程未处理的异常
        Exception ex = e.ExceptionObject as Exception;
        MessageBox.Show("非UI线程未处理异常：" + ex?.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // 处理任务调度程序未观察到的任务异常
        MessageBox.Show("非主线程未处理异常：" + e.Exception.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.SetObserved(); // 防止程序崩溃
    }
}
