using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using XFEToolBox.Tools.SpaceEngineers;

namespace SpaceEngineersPluginManager.Validation;

internal static partial class Program
{
    private static ManagerSettings CreateSettings(string name = "runtime")
    {
        string root = Path.Combine(Fixtures, name);
        string bin = Path.Combine(root, "游戏 路径", "Bin64");
        string loader = Path.Combine(root, "XFE 加载器", "XFE.SpaceEngineers.exe");
        Directory.CreateDirectory(bin); Directory.CreateDirectory(Path.GetDirectoryName(loader)!);
        File.WriteAllBytes(Path.Combine(bin, "SpaceEngineers.exe"), []); File.WriteAllBytes(loader, []);
        string bridge = Path.Combine(Path.GetDirectoryName(loader)!, "XFE.SpaceEngineers.Bridge.dll"); File.WriteAllBytes(bridge, []);
        string profile = Path.Combine(root, "配置 目录", "profile.json"); Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
        File.WriteAllText(profile, "{\"formatVersion\":1,\"plugins\":[]}");
        return new ManagerSettings { Bin64Path = bin, LauncherPath = loader, BridgePath = bridge, ProfilePath = profile };
    }

    private static async Task TestRuntimeAsync()
    {
        var settings = CreateSettings();
        string[] arguments = ["-nosplash", "中文 参数", "", "quote\"inside", @"C:\path with space\", "a\tb"];
        settings.Arguments = arguments.ToList();
        var info = GameRuntime.BuildStartInfo(settings, false);
        Check(info.FileName == settings.LauncherPath && !info.UseShellExecute && info.WorkingDirectory == Path.GetDirectoryName(settings.LauncherPath), "XFE launch uses explicit executable and working directory without shell");
        Check(info.ArgumentList.TakeLast(arguments.Length).SequenceEqual(arguments) && info.Arguments == "", "launch preserves exact Unicode, quotes, empty strings and trailing slash as argument list");
        Check(info.ArgumentList.Contains(settings.Bin64Path) && info.ArgumentList.Contains(settings.ProfilePath) && info.ArgumentList.Contains(settings.BridgePath) && info.ArgumentList[8] == "--", "XFE receives game, bridge and JSON profile separately from game arguments");
        var vanilla = GameRuntime.BuildStartInfo(settings, true);
        Check(vanilla.FileName == Path.Combine(settings.Bin64Path, "SpaceEngineers.exe") && vanilla.ArgumentList.SequenceEqual(arguments), "vanilla launch bypasses loader arguments and uses original game executable");
        foreach (string blocked in new[] { "-plugin", "--PROFILE", "--game-bin64", "--bridge", "invalid\0arg" })
        {
            settings.Arguments = [blocked];
            Throws<ArgumentException>(() => GameRuntime.BuildStartInfo(settings, false), "managed option/NUL rejected: " + blocked.Replace("\0", "[NUL]"));
        }
        settings.Arguments = [];
        string originalLoader = settings.LauncherPath;
        settings.LauncherPath += ".missing";
        Throws<FileNotFoundException>(() => GameRuntime.BuildStartInfo(settings, false), "missing launcher rejected before launch");
        settings.LauncherPath = originalLoader;
        var process = new RunningGame(17, Path.Combine(settings.Bin64Path, "SpaceEngineers.exe"), DateTime.UtcNow);
        var fixtureHost = new FixtureProcessHost(process);
        var runtime = new GameRuntime(fixtureHost);
        Throws<InvalidOperationException>(() => runtime.Launch(settings, false), "running game prevents duplicate launch");
        Check(fixtureHost.Starts.Count == 0 && fixtureHost.RequestedPaths!.All(Path.IsPathFullyQualified), "process search receives exact full installation paths and launch remains untouched");
        fixtureHost.AcceptClose = false;
        await ThrowsAsync<IOException>(() => runtime.CloseAsync(settings), "game without closable window fails without force termination");
        Check(fixtureHost.Alive && fixtureHost.Starts.Count == 0, "failed graceful close does not launch a replacement");
        fixtureHost.AcceptClose = true;
        using (var canceled = new CancellationTokenSource(TimeSpan.FromMilliseconds(80)))
            await ThrowsAsync<OperationCanceledException>(() => runtime.CloseAsync(settings, token: canceled.Token), "close wait can be canceled while original process remains alive");
        Check(fixtureHost.Alive && fixtureHost.Starts.Count == 0, "canceling close does not force-kill or launch game");
        fixtureHost.ExitOnClose = true;
        await runtime.CloseAsync(settings);
        runtime.Launch(settings, false);
        Check(fixtureHost.Starts.Count == 1 && !fixtureHost.Alive, "replacement launch becomes available only after graceful close completes");
        string oldLauncher = Path.Combine(Fixtures, "previous", "XFE.SpaceEngineers.exe"); settings.KnownLauncherPaths.Add(oldLauncher);
        Check(GameRuntime.Executables(settings).Contains(settings.LauncherPath) && GameRuntime.Executables(settings).Contains(oldLauncher), "process matching includes current and previous XFE launcher versions");
        await TestWindowsProcessHostAsync();
    }

    private sealed class FixtureProcessHost(RunningGame game) : IGameProcessHost
    {
        public bool Alive { get; set; } = true;
        public bool AcceptClose { get; set; } = true;
        public bool ExitOnClose { get; set; }
        public Exception? StartFailure { get; set; }
        public int CloseRequests { get; private set; }
        public IReadOnlyList<string>? RequestedPaths { get; private set; }
        public List<ProcessStartInfo> Starts { get; } = [];
        public IReadOnlyList<RunningGame> Find(IReadOnlyList<string> executablePaths) { RequestedPaths = executablePaths; return Alive ? [game] : []; }
        public bool IsAlive(RunningGame candidate) => Alive && candidate == game;
        public bool RequestClose(RunningGame candidate) { if (candidate != game) throw new InvalidOperationException("unexpected fixture PID"); CloseRequests++; if (AcceptClose && ExitOnClose) Alive = false; return AcceptClose; }
        public void Start(ProcessStartInfo startInfo) { Starts.Add(startInfo); if (StartFailure != null) throw StartFailure; }
    }

    private static int RunProcessFixture(string marker)
    {
        var app = new Application();
        var window = new Window { Width = 180, Height = 100, Left = -30000, Top = -30000, ShowInTaskbar = true, ShowActivated = false, Title = "Owned validation fixture" };
        window.Loaded += (_, _) =>
        {
            var ready = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            ready.Tick += (_, _) => { ready.Stop(); File.WriteAllText(marker + ".ready", Environment.ProcessId.ToString()); };
            ready.Start();
        };
        window.Closed += (_, _) => File.WriteAllText(marker + ".closed", "graceful");
        var expiry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        expiry.Tick += (_, _) => app.Shutdown(9); expiry.Start();
        return app.Run(window);
    }

    private static async Task TestWindowsProcessHostAsync()
    {
        string marker = Path.Combine(Fixtures, "owned-window");
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("--process-fixture"); info.ArgumentList.Add(marker);
        using var fixture = Process.Start(info)!;
        try
        {
            var wait = Stopwatch.StartNew();
            while (!File.Exists(marker + ".ready"))
            {
                if (fixture.HasExited || wait.Elapsed > TimeSpan.FromSeconds(12)) throw new InvalidOperationException("Owned process fixture did not become ready: " + await fixture.StandardError.ReadToEndAsync());
                await Task.Delay(30);
            }
            var host = new WindowsGameProcessHost();
            var found = host.Find([Environment.ProcessPath!]);
            var identity = found.Single(game => game.Id == fixture.Id);
            Check(host.IsAlive(identity), "Windows process discovery verifies real owned fixture path and start time");
            Check(!host.IsAlive(identity with { StartTimeUtc = identity.StartTimeUtc.AddSeconds(-1) }) && !host.IsAlive(identity with { ExecutablePath = Path.Combine(Fixtures, "different.exe") }), "PID reuse or executable mismatch cannot match a managed process");
            Check(host.Find([Path.Combine(Fixtures, Path.GetFileName(Environment.ProcessPath)!)]).Count == 0, "same executable name in a different directory never matches process search");
            Check(host.RequestClose(identity), "real owned window accepts normal close message");
            await fixture.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
            Check(fixture.ExitCode == 0 && File.ReadAllText(marker + ".closed") == "graceful" && !host.IsAlive(identity), "normal close runs owned fixture shutdown handler and identity expires");
        }
        finally
        {
            // Only this test's directly spawned fixture can be terminated during failed-test cleanup.
            if (!fixture.HasExited) { fixture.Kill(true); await fixture.WaitForExitAsync(); }
        }
    }
}
