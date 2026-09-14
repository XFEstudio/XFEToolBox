using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using XFEToolBox.Tools.SpaceEngineers;
using XFEToolBox.Tools.SpaceEngineers.ViewModels;
using XFEToolBox.Tools.SpaceEngineers.Views;

namespace SpaceEngineersPluginManager.Validation;

internal static partial class Program
{
    private static async Task TestViewAsync()
    {
        var host = new FixtureProcessHost(new(55, "fixture", DateTime.UtcNow)) { Alive = false };
        using var service = CreateService("view", host, out _);
        var entries = new[] { StoredFixture("CameraInfo"), StoredFixture("GridUtilities"), StoredFixture("ShipMonitor") };
        var store = new PluginProfileStore(); store.Save(store.Load(service.Settings.ProfilePath), entries);
        var vm = new MainPageViewModel(service, pollingEnabled: false);
        var trace = new BindingTrace();
        PresentationTraceSources.DataBindingSource.Listeners.Add(trace);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var page = new MainPage(vm);
        var window = new Window { Content = page, Width = 1080, Height = 760, Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
        try
        {
            window.Show(); await vm.InitializeAsync();
            Check(vm.Plugins.Count == 3 && !vm.IsRunning && vm.LaunchCommand.CanExecute(null), "actual view model loads fixture plugin list and exposes valid launch state");
            vm.Plugins[0].Name = "摄像头与飞行信息"; vm.Plugins[0].Description = "在驾驶舱显示摄像头与航行状态，便于检查大型舰船。";
            vm.Plugins[1].Name = "网格管理辅助"; vm.Plugins[2].Name = "舰船设备监控";
            vm.Plugins.Add(new PluginRow { Id = @"C:\开发项目\AutoMining\plugin.dll", Name = "自动采矿 · 本地开发版", Category = PluginCategory.Local, Location = @"C:\开发项目\AutoMining\plugin.dll", IsEnabled = false });
            vm.SelectedPlugin = vm.Plugins[0];
            vm.Plugins[0].IsEnabled = false;
            Check(vm.IsDirty && vm.ApplyCommand.CanExecute(null), "plugin checkbox updates dirty state and apply availability");
            vm.SelectedPluginFilter = 2;
            Check(vm.PluginsView.Cast<PluginRow>().Count() == 2, "disabled-plugin filter includes actual unchecked rows");
            vm.SelectedPluginFilter = 0; vm.Filter = "网格";
            Check(vm.PluginsView.Cast<PluginRow>().Single().Name == "网格管理辅助", "plugin search handles Chinese display names");
            vm.Filter = "";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveView(page, 1080, 760, "se-plugin-manager-normal.png");
            window.Width = 820; window.Height = 500;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveView(page, 820, 500, "se-plugin-manager-minimum.png");
            var grid = (DataGrid)page.FindName("PluginsGrid");
            Check(grid.ActualHeight >= 80 && page.ActualWidth <= 820 && page.ActualHeight <= 500, "minimum host content keeps plugin grid readable after reserving title-bar height");
            vm.SelectedTab = 1;
            vm.Arguments.Add(new LaunchArgument { Value = "-nosplash" }); vm.Arguments.Add(new LaunchArgument { Value = "中文路径参数" });
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveView(page, 820, 500, "se-plugin-manager-environment-minimum.png");
            window.Width = 1080; window.Height = 760;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveView(page, 1080, 760, "se-plugin-manager-environment.png");
            vm.SelectedTab = 2;
            vm.LogText = "[16:30:02] 已读取插件配置。\n[16:30:03] 原配置已备份。\n[16:30:04] 应用完成；下次启动将加载新的插件清单。";
            vm.Backups.Add(new BackupItem(@"C:\配置\.xfe-backups\profile.json.20260914-163003.json")); vm.SelectedBackup = vm.Backups[0];
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SaveView(page, 1080, 760, "se-plugin-manager-backups.png");
            Check(((TextBox)page.FindName("LogBox")).Text == vm.LogText && vm.RestoreCommand.CanExecute(null), "backup selection and read-only log bindings operate correctly");
            vm.IsBusy = true;
            Check(!vm.ApplyCommand.CanExecute(null) && !vm.LaunchCommand.CanExecute(null) && !vm.ImportDllCommand.CanExecute(null), "in-progress operation disables conflicting mutation/launch controls");
            vm.IsBusy = false;
            Check(trace.Errors.Count == 0, "all tabs render without WPF binding errors: " + string.Join(" | ", trace.Errors));
            Check(host.Starts.Count == 0 && !host.Alive, "UI rendering and interactions never start or close a real game");
        }
        finally
        {
            window.Close(); await vm.ShutdownAsync();
            PresentationTraceSources.DataBindingSource.Listeners.Remove(trace);
        }
    }
}
