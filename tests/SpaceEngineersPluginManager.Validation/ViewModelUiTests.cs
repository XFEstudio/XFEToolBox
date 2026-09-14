using System.Diagnostics;
using System.IO;
using XFEToolBox.Tools.SpaceEngineers;
using XFEToolBox.Tools.SpaceEngineers.ViewModels;

namespace SpaceEngineersPluginManager.Validation;

internal static partial class Program
{
    private static async Task TestViewPollingAsync(LoaderFixture fixture, LoaderInstallation built)
    {
        string source = Path.Combine(fixture.Root, "VM Source"); Directory.CreateDirectory(source);
        string entry = Path.Combine(source, "VmPlugin.dll"); File.Copy(fixture.Plugins[0], entry);
        File.Copy(Path.Combine(Path.GetDirectoryName(fixture.Plugins[0])!, "FixtureDependency.dll"), Path.Combine(source, "FixtureDependency.dll"));
        string options = Path.Combine(source, "options.json"); File.WriteAllText(options, "1");
        var imported = PluginFiles.Import(entry, Path.Combine(fixture.Root, "VM Library"));
        var host = new FixtureProcessHost(new(93, built.LauncherPath, DateTime.UtcNow)) { Alive = true, ExitOnClose = false };
        using var service = CreateService("vm-polling", host, out _);
        service.Settings.Bin64Path = fixture.Bin64; service.Settings.LauncherPath = built.LauncherPath; service.Settings.BridgePath = built.BridgePath;
        service.Settings.RestartOnLocalChanges = true;
        var profiles = new PluginProfileStore(); profiles.Save(profiles.Load(service.Settings.ProfilePath), [imported]);
        var vm = new MainPageViewModel(service, pollingEnabled: false);
        try
        {
            await vm.InitializeAsync(); await vm.PollOnceAsync();
            File.WriteAllText(options, "2"); await vm.PollOnceAsync(); await Task.Delay(3150);
            var synchronization = vm.PollOnceAsync();
            var wait = Stopwatch.StartNew();
            while (host.CloseRequests == 0) { if (wait.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("VM watcher did not begin graceful close"); await Task.Delay(15); }
            Check(vm.IsPolling && !vm.CanEdit && !vm.ApplyCommand.CanExecute(null) && !vm.LaunchCommand.CanExecute(null) && !vm.CloseGameCommand.CanExecute(null), "background synchronization disables editing and conflicting launch/close controls");
            host.Alive = false; await synchronization;
            string synchronized = profiles.Load(service.Settings.ProfilePath).Plugins.Single().AssemblyPath;
            Check(synchronized != imported.AssemblyPath && vm.Plugins.Single().Location == synchronized, "view model refreshes managed DLL path after source synchronization commits");
            vm.Plugins.Single().IsEnabled = false;
            await vm.ApplyCommand.ExecuteAsync(null);
            Check(profiles.Load(service.Settings.ProfilePath).Plugins.Single().AssemblyPath == synchronized && !vm.IsDirty, "subsequent UI apply cannot restore stale pre-synchronization DLL version");
            vm.Plugins.Single().IsEnabled = true; await vm.ApplyCommand.ExecuteAsync(null); await vm.PollOnceAsync();
            byte[] clean = File.ReadAllBytes(service.Settings.ProfilePath);
            vm.Plugins.Single().IsEnabled = false;
            File.WriteAllText(options, "3"); await vm.PollOnceAsync(); await Task.Delay(3150); await vm.PollOnceAsync();
            Check(vm.IsDirty && File.ReadAllBytes(service.Settings.ProfilePath).SequenceEqual(clean), "pending UI checkbox edits pause automatic source synchronization without replacing draft");
            vm.IsDirty = false;
            File.Delete(entry); await vm.PollOnceAsync(); await Task.Delay(3150); await vm.PollOnceAsync();
            Check(!service.Settings.RestartOnLocalChanges && !vm.RestartOnLocalChanges, "failed source synchronization pauses both service watcher and visible UI checkbox");
        }
        finally { await vm.ShutdownAsync(); }
    }
}
