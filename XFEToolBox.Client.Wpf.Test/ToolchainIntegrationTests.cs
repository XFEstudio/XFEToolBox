using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using XFEToolBox.Client.Core.Tools;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Core.Tools;

namespace XFEToolBox.Client.Wpf.Test;

[NonParallel]
public static class ToolchainIntegrationTests
{
    // Explicit: first run downloads the real SDK and NuGet packages. The WPF fixture stays invisible.
    [Test]
    [Explicit]
    public static async Task BuildsAndRunsWpfWithoutSystemDotNet()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        var progress = new InlineProgress();
        var installation = await ToolchainManager.Default.EnsureInstalledAsync(progress, timeout.Token);
        var root = Path.Combine(ToolchainManager.Default.CacheRoot, "smoke-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "workspace");
        Directory.CreateDirectory(Path.Combine(workspace, "Code"));
        var marker = Path.Combine(root, "runtime.txt");
        var environment = new Dictionary<string, string?>
        {
            ["PATH"] = Environment.GetFolderPath(Environment.SpecialFolder.System),
            ["DOTNET_ROOT"] = Path.Combine(root, "missing-dotnet"),
            ["DOTNET_ROOT_X64"] = Path.Combine(root, "missing-dotnet"),
            ["DOTNET_ROOT_X86"] = Path.Combine(root, "missing-dotnet"),
            ["DOTNET_ROOT_ARM64"] = Path.Combine(root, "missing-dotnet"),
            ["DOTNET_ROOT(x86)"] = Path.Combine(root, "missing-dotnet"),
            ["DOTNET_MULTILEVEL_LOOKUP"] = "0",
            ["MSBuildSDKsPath"] = Path.Combine(root, "missing-sdk")
        };
        var originalEnvironment = environment.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            var manifest = new ToolPackageManifest
            {
                Id = "xfestudio.private-toolchain-smoke", Name = "Private runtime smoke", Version = "1.0.0",
                Description = "Invisible WPF compilation fixture", Author = "XFEstudio",
                Entry = new ToolEntryManifest { ViewXaml = "Code/Main.xaml", ViewCodeBehind = "Code/Main.xaml.cs", ViewClass = "PrivateRuntimeSmoke.Main" }
            };
            await File.WriteAllTextAsync(Path.Combine(workspace, "manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)), timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(workspace, "Code/Main.xaml"), """
                <Window x:Class="PrivateRuntimeSmoke.Main" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Opacity="0" ShowInTaskbar="False" ShowActivated="False">
                    <TextBlock Text="{Binding Value}" />
                </Window>
                """, timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(workspace, "Code/Main.xaml.cs"), $$"""
                using System.IO;
                using System.Windows;
                using System.Windows.Threading;
                using CommunityToolkit.Mvvm.ComponentModel;
                namespace PrivateRuntimeSmoke;
                public partial class Model : ObservableObject
                {
                    [ObservableProperty] private string value = "source-generator-ok";
                }
                public partial class Main : Window
                {
                    public Main()
                    {
                        InitializeComponent();
                        var model = new Model();
                        DataContext = model;
                        Loaded += (_, _) =>
                        {
                            File.WriteAllLines({{JsonSerializer.Serialize(marker)}}, new[]
                            {
                                typeof(object).Assembly.Location, typeof(Window).Assembly.Location, model.Value
                            });
                            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                            timer.Tick += (_, _) => { timer.Stop(); Application.Current.Shutdown(); };
                            timer.Start();
                        };
                    }
                }
                """, timeout.Token);

            foreach (var (key, value) in environment) Environment.SetEnvironmentVariable(key, value);
            var build = await ToolProjectRunService.BuildAsync(workspace, manifest, timeout.Token, progress);
            Ensure(build.Success, build.Message);
            Console.WriteLine("PASS Code Studio build with unavailable system dotnet");
            var preview = await ToolProjectRunService.BuildAndRunAsync(workspace, manifest, timeout.Token, progress);
            await VerifyRuntimeAsync(preview, marker, installation, timeout.Token);

            File.Delete(marker);
            var package = Path.Combine(root, "smoke.xfetool");
            ZipFile.CreateFromDirectory(workspace, package);
            var sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(package, timeout.Token)));
            var opened = await ToolProjectRunService.BuildPackageAndRunAsync(package, manifest.Id, manifest.Version, sha256,
                cancellationToken: timeout.Token, progress: progress);
            await VerifyRuntimeAsync(opened, marker, installation, timeout.Token);
            Console.WriteLine("PASS Code Studio preview and downloaded package: WPF, XAML, source generator, private CLR and WPF runtime");
        }
        finally
        {
            foreach (var (key, value) in originalEnvironment) Environment.SetEnvironmentVariable(key, value);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task VerifyRuntimeAsync(ToolRunResult result, string marker, ToolchainInstallation installation, CancellationToken token)
    {
        Ensure(result.Success && result.ProcessId is not null, result.Message);
        using var process = Process.GetProcessById(result.ProcessId!.Value);
        var runtimeRoot = Path.GetDirectoryName(Path.GetDirectoryName(process.MainModule!.FileName))!;
        try
        {
            await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(30), token);
            Ensure(process.ExitCode == 0, "工具运行失败。");
            var lines = await File.ReadAllLinesAsync(marker, token);
            Ensure(lines.Length == 3 && lines.Take(2).All(line => line.StartsWith(installation.Root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)), "工具没有加载私有运行时：" + string.Join("; ", lines));
            Ensure(lines[2] == "source-generator-ok", "NuGet 源生成器没有正常工作。");
            Console.WriteLine(string.Join(Environment.NewLine, lines));
            // The host also waits for a possible elevated replacement before removing the build directory.
            for (var attempt = 0; attempt < 100 && Directory.Exists(runtimeRoot); attempt++)
                await Task.Delay(100, token);
            Ensure(!Directory.Exists(runtimeRoot), "工具退出后未清理生成目录。");
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class InlineProgress : IProgress<ToolPreparationProgress>
    {
        public void Report(ToolPreparationProgress value) => Console.WriteLine(value.Message);
    }
}
