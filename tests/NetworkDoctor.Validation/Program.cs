using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using XFEToolBox.Core.Tools;
using XFEToolBox.Tools.NetworkDoctor;

namespace NetworkDoctor.Validation;

internal static class Program
{
    private static readonly string Workspace = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XFEToolBox", "CrossVersion", "EditorWorkspaces", "NetworkDoctor");
    private static readonly string Artifacts = Path.Combine(AppContext.BaseDirectory, "test-artifacts");
    private static readonly List<string> Results = [];
    private static int exitCode;
    [STAThread]
    private static int Main(string[] args)
    {
        Directory.CreateDirectory(Artifacts);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/XFEToolBox.WpfCore;component/Resources/Style/ToolThemeResources.xaml") });
        app.Startup += async (_, _) => {
            try { await RunAsync(args); }
            catch (Exception e) { exitCode = 1; Console.Error.WriteLine(e); }
            finally { File.WriteAllText(Path.Combine(Artifacts, "results.json"), JsonSerializer.Serialize(new { passed = exitCode == 0, time = DateTimeOffset.Now, checks = Results }, new JsonSerializerOptions { WriteIndented = true })); app.Shutdown(); }
        }; app.Run(); return exitCode;
    }
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException("FAIL: " + message); Results.Add(message); Console.WriteLine("PASS: " + message); }
    private static NetworkSnapshot Fixture() => new() {
        Adapters = [new("7f975253-371a-4fe6-b12f-31baac0ccafa", "测试网卡（仅模拟）", "Test adapter", "Ethernet", true, 8, true, ["169.254.1.2"], ["127.0.0.1"], ["192.0.2.1"], 1000000000, 0)],
        System = new() { Services = [new("Dhcp", "Stopped", "Automatic"), new("Dnscache", "Stopped", "Disabled"), new("MpsSvc", "Running", "Automatic")], Addresses = [new("192.0.2.5", "Duplicate", 8)], Routes = [new("0.0.0.0/0", "192.0.2.1", 8, 20), new("0.0.0.0/0", "192.0.2.2", 9, 10)], Interfaces = [new(8,"IPv6",1000,10)], Firewall = [new("Public","False","Allow")], Nrpt = ["企业分流"], Events = ["历史 DNS 超时事件"] },
        ProxyEnabled = true, ProxyServer = "127.0.0.1:7890", HostsEntries = ["0.0.0.0 www.microsoft.com"], WinsockReadable = true, ActiveProbing = false, TimeWait = 9000
    };
    private static async Task RunAsync(string[] args)
    {
        MakeIcon();
        var s = Fixture(); var checks = NetworkScanner.Evaluate(s).ToList();
        Assert(checks.Single(c => c.Code == "duplicate-ip").Health == Health.Error, "Windows Duplicate address classified as confirmed error");
        Assert(checks.Single(c => c.Code.StartsWith("ip-")).Health == Health.Warning, "APIPA classified as clue, not blanket total connectivity failure");
        Assert(checks.Single(c => c.Code == "route").Health == Health.Info, "multiple default routes not declared broken");
        Assert(checks.Single(c => c.Code == "user-proxy").Health == Health.Info, "configured proxy not declared broken");
        Assert(checks.Single(c => c.Code == "nrpt").Health == Health.Info, "enterprise DNS rules not declared broken");
        Assert(checks.Single(c => c.Code == "events").Health == Health.Info, "historical errors not treated as current failures");
        Assert(checks.Single(c => c.Code == "hosts").Health == Health.Warning, "target hosts override reported without deleting it");
        Assert(checks.Single(c => c.Code == "mtu-config").Health == Health.Warning, "low IPv6 MTU flagged");
        Assert(checks.Single(c => c.Code == "firewall").Health == Health.Warning, "disabled firewall reported without disabling security");
        Assert(checks.Single(c => c.Code == "tcp-count").Health == Health.Warning, "high TIME_WAIT classified as clue");
        Assert(NetworkScanner.Evaluate(new()).Single(c => c.Code == "duplicate-ip").Health == Health.Unknown, "missing IP data not reported healthy");
        var v6 = Fixture(); v6.Adapters = [v6.Adapters[0] with { Dhcp = false, Addresses = ["2001:db8::1"], Gateways = ["fe80::1"] }];
        Assert(NetworkScanner.Evaluate(v6).Single(c => c.Code.StartsWith("ip-")).Health == Health.Info, "IPv6-only network not treated as DHCP failure");
        foreach (string host in new[] { "https://example.com", "example.com/path", "example.com:443", "a;whoami", "*.example.com" })
        { bool rejected = false; try { NetworkScanner.ValidateHost(host); } catch { rejected = true; } Assert(rejected, "invalid target rejected: " + host); }
        Assert(NetworkScanner.ValidateHost("例子.测试").StartsWith("xn--"), "international DNS name converted through IDNA");
        Assert(!SnapshotCollector.Redact("http://user:secret@host:8080/proxy?token=secret").Contains("secret"), "proxy credentials and query secrets redacted");
        Assert(SnapshotCollector.Redact("") == "", "absent PAC not misreported as configured");
        checks.Add(new("dns-system", "DNS", "系统 DNS", Health.Error, "解析失败", "模拟"));
        var plan = RepairEngine.Plan(s, checks);
        Assert(plan.Where(p => p.Selected).Select(p => p.Kind).ToHashSet().SetEquals([RepairKind.FlushDns, RepairKind.StartDhcp]), "only evidence-based low-impact repairs recommended");
        Assert(plan.Where(p => p.Disruptive || p.RequiresRestart).All(p => !p.Selected), "all disruptive/reset repairs opt-in");
        Assert(plan.All(p => p.Kind != RepairKind.StartDns), "disabled services excluded from recommendations");
        Assert(RepairEngine.Plan(v6, checks).All(p => p.Kind != RepairKind.RenewDhcp), "static/IPv6-only adapter not offered DHCP renew");
        var options = new RepairOption[] { new() { Kind=RepairKind.FlushDns, Title="flush", Description="" }, new() { Kind=RepairKind.ResetWinsock, Title="winsock", Description="" } };
        foreach (var option in plan)
        {
            var command = RepairEngine.BuildCommand(option, s);
            Assert(command.Mutates && Path.IsPathFullyQualified(command.Executable), "allowlisted absolute command: " + option.Kind);
        }
        await ValidateScriptsAsync(plan, s);
        var renew = plan.Single(p => p.Kind == RepairKind.RenewDhcp);
        string script = Encoding.Unicode.GetString(Convert.FromBase64String(RepairEngine.BuildCommand(renew, s).Arguments[^1]));
        Assert(script.Contains("DHCPEnabled") && script.Contains(s.Adapters[0].Id) && !script.Contains("/release"), "DHCP renew revalidates exact GUID and avoids release-all");
        await RejectRepairAsync(options, s, false, true, "repair rejects missing administrator permission");
        await RejectRepairAsync(options, s, true, false, "repair rejects missing confirmation");
        var stale = Fixture(); stale.CapturedAt = DateTimeOffset.Now.AddHours(-1);
        await RejectRepairAsync(options, stale, true, true, "stale snapshot rejected before mutation");
        await RejectRepairAsync([new() { Kind = RepairKind.RestartAdapter, AdapterId="';evil",Title="bad",Description="" }], s, true, true, "invalid adapter identifier rejected before mutation");
        await RejectRepairAsync([new() { Kind=(RepairKind)999,Title="bad",Description="" }],s,true,true,"unknown repair rejected");
        var fake = new FakeRunner();
        string repairRoot = Path.Combine(Artifacts, "repairs");
        fake.OnRun = command => { if (command.Mutates) Assert(Directory.GetFiles(repairRoot, "before.json", SearchOption.AllDirectories).Length > 0, "backup exists before mutation"); return new(0,"simulated success",""); };
        var repair = await RepairEngine.ExecuteAsync(options,s,fake,repairRoot,true,true,new InlineProgress<string>(_=>{}),default);
        Assert(repair.Steps.All(x => x.CommandSucceeded) && repair.Steps[1].RestartRequired && File.Exists(Path.Combine(repair.BackupDirectory,"result.json")), "repair success journal and restart requirement retained");
        fake = new FakeRunner { OnRun = _ => new(5,"","simulated access denied") };
        var failed = await RepairEngine.ExecuteAsync([options[0]],s,fake,repairRoot,true,true,new InlineProgress<string>(_=>{}),default);
        Assert(!failed.Steps[0].CommandSucceeded, "nonzero exit code never reported as repaired");
        fake = new FakeRunner { OnRun = _ => new(-1,"","timeout",true) };
        var timeout = await RepairEngine.ExecuteAsync([options[0]],s,fake,repairRoot,true,true,new InlineProgress<string>(_=>{}),default);
        Assert(!timeout.Steps[0].CommandSucceeded, "timeout result not treated as success");
        using (var cts = new CancellationTokenSource())
        {
            fake = new FakeRunner { OnRun = _ => { cts.Cancel(); return new(0,"first step done",""); } };
            var cancelled = await RepairEngine.ExecuteAsync(options,s,fake,repairRoot,true,true,new InlineProgress<string>(_=>{}),cts.Token);
            Assert(cancelled.Cancelled && fake.Commands.Count == 1 && cancelled.Steps[0].CommandSucceeded, "cancellation finishes current step but skips later steps");
        }
        fake = new FakeRunner { OnRun = command => new(1,"","backup unavailable") };
        bool backupRejected=false;
        try { await RepairEngine.ExecuteAsync([plan.Single(p=>p.Kind==RepairKind.ResetWinHttp)],s,fake,repairRoot,true,true,new InlineProgress<string>(_=>{}),default); } catch(IOException) {backupRejected=true;}
        Assert(backupRejected && fake.Commands.All(c=>!c.Mutates), "WinHTTP backup failure prevents mutation");
        await ProbeTestsAsync();
        await CommandTestsAsync();
        await ViewTestsAsync(s,checks,plan);
        if(args.Contains("--live")) await LiveAsync();
        await HostAsync(args);
        Console.WriteLine($"ALL {Results.Count} CHECKS PASSED. Artifacts: {Artifacts}");
    }
    private static async Task RejectRepairAsync(RepairOption[] options,NetworkSnapshot s,bool admin,bool confirmed,string message)
    {
        var fake=new FakeRunner(); bool rejected=false;
        try {await RepairEngine.ExecuteAsync(options,s,fake,Path.Combine(Artifacts,"reject"),admin,confirmed,new InlineProgress<string>(_=>{}),default);} catch {rejected=true;}
        Assert(rejected && fake.Commands.Count==0,message);
    }
    private static async Task ValidateScriptsAsync(List<RepairOption> plan, NetworkSnapshot snapshot)
    {
        var commands = plan.Select(p => RepairEngine.BuildCommand(p, snapshot)).Append(WindowsCommands.PowerShell(SnapshotCollector.SystemScript));
        foreach (var command in commands.Where(c => c.Executable.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase)))
        {
            string code = "$src=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('" + command.Arguments[^1] + "')); $tokens=$null; $errors=$null; [void][System.Management.Automation.Language.Parser]::ParseInput($src,[ref]$tokens,[ref]$errors); if($errors.Count -gt 0){throw ($errors | Out-String)}; 'Parsed without executing the script'";
            var result = await new WindowsCommands().RunAsync(WindowsCommands.PowerShell(code), default);
            Assert(result.Success, "Windows PowerShell production script syntax: " + result.Text);
        }
    }
    private static async Task ProbeTestsAsync()
    {
        byte[] query=NetworkProbes.DnsQuery("example.com",123), reply=Response(query);
        Assert(NetworkProbes.ValidateDns(query,reply).Answers==1,"valid compressed DNS answer parsed");
        foreach(var invalid in new[]{reply[..10],reply[..^1],reply.Select((b,i)=>(byte)(i==0?b^1:b)).ToArray()})
        {bool rejected=false;try{NetworkProbes.ValidateDns(query,invalid);}catch(InvalidDataException){rejected=true;}Assert(rejected,"malformed/mismatched DNS packet rejected");}
        using var udp=new UdpClient(new IPEndPoint(IPAddress.Loopback,0)); int port=((IPEndPoint)udp.Client.LocalEndPoint!).Port;
        var udpTask=Task.Run(async()=>{var request=await udp.ReceiveAsync();await udp.SendAsync(Response(request.Buffer),request.RemoteEndPoint);});
        var dns=await NetworkProbes.DnsServerAsync(IPAddress.Loopback,"example.com",default,port);await udpTask;
        Assert(dns.Health==Health.Pass,"real UDP DNS exchange on loopback");
        var fallbackServer = new TcpListener(IPAddress.Loopback, 0); fallbackServer.Start();
        int fallbackPort = ((IPEndPoint)fallbackServer.LocalEndpoint).Port;
        using var fallbackUdp = new UdpClient(new IPEndPoint(IPAddress.Loopback, fallbackPort));
        using var fallbackDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var fallbackServing = Task.Run(async () => {
            var request = await fallbackUdp.ReceiveAsync(fallbackDeadline.Token);
            byte[] tc = request.Buffer.ToArray(); tc[2] = 0x83; tc[3] = 0x80;
            await fallbackUdp.SendAsync(tc, request.RemoteEndPoint, fallbackDeadline.Token);
            using var socket = await fallbackServer.AcceptTcpClientAsync(fallbackDeadline.Token);
            using var stream = socket.GetStream(); byte[] length = new byte[2];
            await stream.ReadExactlyAsync(length, fallbackDeadline.Token);
            byte[] tcpQuery = new byte[BinaryPrimitives.ReadUInt16BigEndian(length)];
            await stream.ReadExactlyAsync(tcpQuery, fallbackDeadline.Token);
            byte[] answer = Response(tcpQuery); BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)answer.Length);
            await stream.WriteAsync(length, fallbackDeadline.Token); await stream.WriteAsync(answer.AsMemory(0, 7), fallbackDeadline.Token);
            await Task.Delay(20, fallbackDeadline.Token); await stream.WriteAsync(answer.AsMemory(7), fallbackDeadline.Token);
        });
        var fallbackResult = await NetworkProbes.DnsServerAsync(IPAddress.Loopback, "example.com", fallbackDeadline.Token, fallbackPort);
        await fallbackServing; fallbackServer.Stop(); Assert(fallbackResult.Health == Health.Pass, "truncated UDP DNS falls back to length-framed TCP with partial reads");
        var tcp=new TcpListener(IPAddress.Loopback,0);tcp.Start();int tcpPort=((IPEndPoint)tcp.LocalEndpoint).Port;
        var accepted=tcp.AcceptTcpClientAsync();var success=await NetworkProbes.TcpAsync("test",IPAddress.Loopback,tcpPort,default);using(var socket=await accepted){}
        tcp.Stop();Assert(success.Health==Health.Pass,"real TCP open endpoint succeeds");
        var refused=await NetworkProbes.TcpAsync("test",IPAddress.Loopback,tcpPort,default);Assert(refused.Health==Health.Warning,"real closed TCP endpoint reported without claiming total outage");
        foreach(var item in new[]{(Status:200,Body:"Microsoft Connect Test",Portal:true,Expected:Health.Pass),(Status:302,Body:"login",Portal:true,Expected:Health.Warning),(Status:403,Body:"forbidden",Portal:false,Expected:Health.Pass),(Status:407,Body:"proxy auth",Portal:false,Expected:Health.Warning)})
        {
            var server=new TcpListener(IPAddress.Loopback,0);server.Start();int p=((IPEndPoint)server.LocalEndpoint).Port;
            var serving=Task.Run(async()=>{using var socket=await server.AcceptTcpClientAsync();using var stream=socket.GetStream();byte[] buffer=new byte[4096];int received=await stream.ReadAsync(buffer);if(received==0)throw new IOException("Empty HTTP fixture request");byte[] body=Encoding.UTF8.GetBytes(item.Body);await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {item.Status} Test\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"));await stream.WriteAsync(body);});
            var check=await NetworkProbes.HttpAsync(new Uri($"http://127.0.0.1:{p}/"),false,item.Portal,default);await serving;server.Stop();
            Assert(check.Health==item.Expected,"HTTP/NCSI classification: "+item.Status+" portal="+item.Portal);
        }
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();bool cancelled=false;
        try{await NetworkProbes.TcpAsync("cancel",IPAddress.Loopback,9,cancellation.Token);}catch(OperationCanceledException){cancelled=true;}
        Assert(cancelled,"probe cancellation propagates rather than false fault result");
    }
    private static byte[] Response(byte[] query)
    {
        byte[] reply=new byte[query.Length+16];query.CopyTo(reply,0);reply[2]=0x81;reply[3]=0x80;reply[7]=1;
        new byte[]{0xc0,12,0,1,0,1,0,0,0,60,0,4,192,0,2,1}.CopyTo(reply,query.Length);return reply;
    }
    private static async Task CommandTestsAsync()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); var oem = Encoding.GetEncoding(936);
        const string chinese = "当前 WinHTTP 代理服务器设置：直接访问";
        Assert(WindowsCommands.DecodeOutput(Encoding.UTF8.GetBytes(chinese), oem) == chinese, "native UTF-8 Chinese output decoded");
        Assert(WindowsCommands.DecodeOutput(oem.GetBytes(chinese), oem) == chinese, "native OEM Chinese output decoded");
        Assert(WindowsCommands.DecodeOutput([255,254,..Encoding.Unicode.GetBytes(chinese)], oem) == chinese, "UTF-16 BOM output decoded");
        var real = new WindowsCommands();
        var unicode = await real.RunAsync(WindowsCommands.PowerShell("[Console]::WriteLine('网络测试'); [Console]::Error.WriteLine('错误流测试')"), default);
        Assert(unicode.Success && unicode.Output.Contains("网络测试") && unicode.Error.Contains("错误流测试"), "real command captures separate Unicode stdout and stderr");
        var nonzero = await real.RunAsync(WindowsCommands.PowerShell("exit 7"), default);
        Assert(nonzero.ExitCode == 7 && !nonzero.Success, "real nonzero child exit preserved");
        var watch = Stopwatch.StartNew();
        var timed = await real.RunAsync(WindowsCommands.PowerShell("Start-Sleep -Seconds 10", timeout:1), default);
        Assert(timed.TimedOut && !timed.Success && watch.Elapsed < TimeSpan.FromSeconds(5), "real helper timeout bounded and own child stopped");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)); bool cancelled = false;
        try { await real.RunAsync(WindowsCommands.PowerShell("Start-Sleep -Seconds 10"), cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
        Assert(cancelled, "real command cancellation propagates");
        var big = await real.RunAsync(WindowsCommands.PowerShell("[Console]::Write([string]::new([char]'x',1100000)); [Console]::Error.Write('done')"), default);
        Assert(big.Success && big.Output.Length < 1050000 && big.Output.Contains("输出已截断") && big.Error == "done", "oversized output bounded while both pipes drain");
        var unreadable = await SnapshotCollector.CaptureAsync(new FakeRunner { OnRun = _ => throw new IOException("simulated helper unavailable") }, default);
        Assert(unreadable.ReadErrors.Length >= 2 && !unreadable.WinsockReadable && unreadable.Adapters.Length > 0, "unavailable command preserves partial read-only snapshot");
    }
    private static async Task ViewTestsAsync(NetworkSnapshot s,List<Check> checks,List<RepairOption> plan)
    {
        var trace=new BindingTrace();PresentationTraceSources.DataBindingSource.Listeners.Add(trace);PresentationTraceSources.DataBindingSource.Switch.Level=SourceLevels.Error;
        var page=new MainPage();var vm=(MainPageViewModel)page.DataContext;
        foreach(var check in checks)vm.Checks.Add(check);foreach(var option in plan)vm.Repairs.Add(option);vm.SelectedCheck=checks.Single(c=>c.Code=="duplicate-ip");vm.ProgressValue=1;vm.Phase="测试数据预览";vm.Status="当前展示模拟故障，未修改网络。";vm.Report=MainPageViewModel.BuildReport(s,checks);
        var window=new Window{Content=page,Width=1180,Height=780,Left=-30000,Top=-30000,WindowStyle=WindowStyle.None,ShowInTaskbar=false};window.Show();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);SaveView(page,"network-doctor.png");
        vm.SelectedTab=1;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);SaveView(page,"network-doctor-repairs.png");
        window.Width=920;window.Height=650;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);SaveView(page,"network-doctor-minimum.png");
        Assert(page.ActualWidth<=920,"minimum layout rendered");
        vm.IsRepairing=true;var closing=new System.ComponentModel.CancelEventArgs();vm.OnClosing(closing);Assert(closing.Cancel,"closing blocked during active repair to avoid half-finished adapter restart");vm.IsRepairing=false;
        vm.SelectedTab=2;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Assert(trace.Errors.Count==0,"no WPF binding errors: "+string.Join(" | ",trace.Errors));
        window.Close();PresentationTraceSources.DataBindingSource.Listeners.Remove(trace);
    }
    private static async Task LiveAsync()
    {
        var runner=new ReadOnlyRunner(); var vm=new MainPageViewModel(runner); int ticks=0;
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(20) }; timer.Tick+=(_,_)=>ticks++; timer.Start();
        vm.Checks.CollectionChanged+=(_,e)=>{if(e.NewItems is not null)foreach(Check c in e.NewItems)Console.WriteLine("LIVE: "+c.Title+" => "+c.State+" / "+c.Summary);};
        await vm.ScanCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(80)); timer.Stop();
        Assert(vm.Checks.Count>15 && vm.ProgressValue==1 && !vm.IsBusy && vm.Report.Contains("配置快照"),"real machine read-only scan returns layered results");
        Assert(ticks>10, "UI dispatcher remains responsive during real scan");
        Assert(vm.Repairs.Count>0 && vm.Repairs.Where(r=>r.Disruptive).All(r=>!r.Selected), "real scan populates guarded repair choices");
        Assert(runner.Commands.All(c=>!c.Mutates),"live validation executed no repair or network configuration changes");
        File.WriteAllText(Path.Combine(Artifacts,"live-report.txt"),vm.Report);
    }
    private static async Task HostAsync(string[] args)
    {
        var assembly=typeof(XFEToolBox.Client.Models.LauncherItem).Assembly;var service=assembly.GetType("XFEToolBox.Client.Utilities.ToolProjectRunService",true)!;
        var manifest=JsonSerializer.Deserialize<ToolPackageManifest>(File.ReadAllText(Path.Combine(Workspace,"manifest.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        async Task Build(string root){var task=(Task)service.GetMethod("BuildAsync",BindingFlags.Public|BindingFlags.Static)!.Invoke(null,[root,manifest,CancellationToken.None])!;await task.WaitAsync(TimeSpan.FromMinutes(3));var result=task.GetType().GetProperty("Result")!.GetValue(task)!;Assert((bool)result.GetType().GetProperty("Success")!.GetValue(result)!,"production host compilation: "+result.GetType().GetProperty("Message")!.GetValue(result));}
        await Build(Workspace);
        if(args.Contains("--check-package")){string package=Path.Combine(Path.GetDirectoryName(Workspace)!,"Packages",$"{manifest.Id}-{manifest.Version}.xfetool"),extracted=Path.Combine(Artifacts,"package-"+Guid.NewGuid().ToString("N"));await (Task<ToolPackageManifest>)service.GetMethod("ExtractAndValidatePackageAsync",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[package,extracted,manifest.Id,manifest.Version,CancellationToken.None])!;var files=Directory.GetFiles(extracted,"*",SearchOption.AllDirectories);Assert(files.Length==Directory.GetFiles(Workspace,"*",SearchOption.AllDirectories).Length&&files.All(f=>File.ReadAllBytes(f).SequenceEqual(File.ReadAllBytes(Path.Combine(Workspace,Path.GetRelativePath(extracted,f))))),"package matches tested sources");await Build(extracted);}
        if(args.Contains("--register"))await (Task)assembly.GetType("XFEToolBox.Client.Utilities.ToolProjectWorkspaceService",true)!.GetMethod("RememberProjectAsync",BindingFlags.Public|BindingFlags.Static)!.Invoke(null,[Workspace])!;
    }
    private static void SaveView(FrameworkElement page,string name){page.UpdateLayout();var image=new RenderTargetBitmap((int)page.ActualWidth,(int)page.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(page);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(Artifacts,name));encoder.Save(file);}
    private static void MakeIcon()
    {
        var v=new DrawingVisual();using(var d=v.RenderOpen()){
            d.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(42,194,235),Color.FromRgb(76,70,197),65),null,new Rect(9,9,106,106),24,24);
            var pen=new Pen(new SolidColorBrush(Color.FromArgb(235,245,252,255)),4);d.DrawEllipse(null,pen,new Point(62,60),36,36);d.DrawEllipse(null,pen,new Point(62,60),17,36);d.DrawLine(pen,new Point(26,60),new Point(98,60));d.DrawLine(pen,new Point(32,43),new Point(92,43));d.DrawLine(pen,new Point(32,77),new Point(92,77));
            d.DrawEllipse(new LinearGradientBrush(Color.FromRgb(129,237,165),Color.FromRgb(21,160,132),90),new Pen(Brushes.White,3),new Point(97,98),23,23);
            var check=new Pen(Brushes.White,6){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};d.DrawLine(check,new Point(85,99),new Point(94,107));d.DrawLine(check,new Point(94,107),new Point(109,90));}
        var bitmap=new RenderTargetBitmap(128,128,96,96,PixelFormats.Pbgra32);bitmap.Render(v);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));Directory.CreateDirectory(Path.Combine(Workspace,"Assets"));using var file=File.Create(Path.Combine(Workspace,"Assets","icon.png"));encoder.Save(file);
    }
    private sealed class FakeRunner:ICommandRunner{public List<CommandSpec> Commands{get;}=[];public Func<CommandSpec,CommandResult> OnRun{get;set;}=_=>new(0,"ok","");public Task<CommandResult> RunAsync(CommandSpec c,CancellationToken t){Commands.Add(c);return Task.FromResult(OnRun(c));}}
    private sealed class ReadOnlyRunner:ICommandRunner{private readonly WindowsCommands real=new();public List<CommandSpec> Commands{get;}=[];public Task<CommandResult> RunAsync(CommandSpec c,CancellationToken t){if(c.Mutates)throw new InvalidOperationException("Live tests forbid mutations");Commands.Add(c);return real.RunAsync(c,t);}}
    private sealed class InlineProgress<T>(Action<T> action):IProgress<T>{public void Report(T value)=>action(value);}
    private sealed class BindingTrace:TraceListener{public List<string> Errors{get;}=[];public override void Write(string? text){if(!string.IsNullOrEmpty(text))Errors.Add(text);}public override void WriteLine(string? text)=>Write(text);}
}
