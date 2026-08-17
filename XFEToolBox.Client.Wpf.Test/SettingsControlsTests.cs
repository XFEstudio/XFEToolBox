using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using XFEToolBox.WpfCore.Controls;

namespace XFEToolBox.Client.Wpf.Test;

public static class SettingsControlsTests
{
    [Test]
    public static void RemoteInputsAreOnlyVisibleWhileRemoteModeIsEnabled()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var remoteModeSwitch = new SwitchButton { IsChecked = false, Margin = new Thickness(0) };
                var addressEditor = new TextEditor { Text = "ws://localhost:3280/" };
                var passwordEditor = new PasswordEditor { Password = "test-password" };
                var addressCard = new SettingsCard
                {
                    Header = "远程服务器地址",
                    Description = "支持 ws:// 与 wss:// 地址",
                    Content = addressEditor
                };
                var passwordCard = new SettingsCard
                {
                    Header = "远程服务器连接密码",
                    Content = passwordEditor
                };
                var expander = new SettingsExpander
                {
                    Header = "远程调试模式",
                    Description = "由工具箱主动连接调试程序服务器",
                    Content = remoteModeSwitch,
                    IsHeaderClickEnabled = false
                };
                expander.Items.Add(addressCard);
                expander.Items.Add(passwordCard);
                BindingOperations.SetBinding(expander, SettingsExpander.IsExpandedProperty, new Binding(nameof(ToggleButton.IsChecked))
                {
                    Source = remoteModeSwitch,
                    Mode = BindingMode.OneWay
                });

                var expandedCount = 0;
                var collapsedCount = 0;
                expander.Expanded += (_, _) => expandedCount++;
                expander.Collapsed += (_, _) => collapsedCount++;

                var window = new Window
                {
                    Width = 720,
                    Height = 420,
                    Left = -10_000,
                    Top = -10_000,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Content = expander
                };
                window.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/XFEToolBox.WpfCore;component/Resources/Style/ToolThemeResources.xaml", UriKind.Relative)
                });
                window.Show();
                PumpDispatcher(dispatcher);
                window.UpdateLayout();

                expander.ApplyTemplate();
                addressCard.ApplyTemplate();
                passwordCard.ApplyTemplate();
                var itemsSite = (Border?)expander.Template.FindName("ItemsSite", expander);
                Ensure(expander.Template is not null && addressCard.Template is not null && passwordCard.Template is not null,
                    "SettingsCard 或 SettingsExpander 没有加载统一主题模板。");
                Ensure(itemsSite is not null, "SettingsExpander 模板缺少内部设置区域。");
                var realizedItemsSite = itemsSite!;
                Ensure(expander.Items.Count == 2, "SettingsExpander 没有保留内部设置项。");
                Ensure(realizedItemsSite.Visibility == Visibility.Collapsed && !addressEditor.IsVisible && !passwordEditor.IsVisible,
                    "远程调试关闭时，远程服务器设置仍然可见或可输入。");

                remoteModeSwitch.IsChecked = true;
                PumpDispatcher(dispatcher);
                window.UpdateLayout();
                Ensure(expander.IsExpanded && realizedItemsSite.Visibility == Visibility.Visible,
                    "打开远程调试后，远程服务器设置没有展开。");
                Ensure(addressEditor.IsVisible && passwordEditor.IsVisible && addressEditor.IsEnabled && passwordEditor.IsEnabled,
                    "打开远程调试后，远程服务器地址或密码仍不可输入。");

                remoteModeSwitch.IsChecked = false;
                PumpDispatcher(dispatcher);
                window.UpdateLayout();
                Ensure(!expander.IsExpanded && realizedItemsSite.Visibility == Visibility.Collapsed,
                    "关闭远程调试后，远程服务器设置没有收起。");
                Ensure(expandedCount == 1 && collapsedCount == 1,
                    $"SettingsExpander 展开/收起事件次数异常：{expandedCount}/{collapsedCount}。");

                window.Close();
                dispatcher.InvokeShutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Ensure(thread.Join(TimeSpan.FromSeconds(15)), "SettingsExpander 远程模式联动测试超时。");
        if (failure is not null)
        {
            Console.WriteLine(failure);
            throw new InvalidOperationException("SettingsExpander 没有正确限制远程设置的输入状态。", failure);
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void PumpDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        _ = dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

}
