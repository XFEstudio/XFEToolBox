using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using XFEToolBox.Core.Model;
using XFEToolBox.Core.Tools;

namespace XFEToolBox.Client.Utilities;

internal static class ToolProjectRunService
{
    public static async Task<ToolRunResult> BuildAndRunAsync(
        string workspaceRoot,
        ToolPackageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        var runtimeRoot = Path.Combine(Path.GetTempPath(), "XFEToolBox", "CodeStudioRuns", Guid.NewGuid().ToString("N"));
        var outputRoot = Path.Combine(runtimeRoot, "output");
        Directory.CreateDirectory(runtimeRoot);

        try
        {
            var assemblyName = $"XFEToolRuntime_{Guid.NewGuid():N}";
            var projectPath = Path.Combine(runtimeRoot, "ToolRuntime.csproj");
            var entryPath = Path.Combine(runtimeRoot, "RuntimeEntry.g.cs");
            await File.WriteAllTextAsync(projectPath, CreateProjectFile(workspaceRoot, assemblyName), new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(entryPath, CreateRuntimeEntry(manifest), new UTF8Encoding(false), cancellationToken);

            var buildInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = runtimeRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            buildInfo.ArgumentList.Add("build");
            buildInfo.ArgumentList.Add(projectPath);
            buildInfo.ArgumentList.Add("--nologo");
            buildInfo.ArgumentList.Add("--output");
            buildInfo.ArgumentList.Add(outputRoot);
            buildInfo.ArgumentList.Add("--property:UseSharedCompilation=false");
            buildInfo.ArgumentList.Add("--property:RestoreIgnoreFailedSources=true");

            using var buildProcess = Process.Start(buildInfo)
                                     ?? throw new InvalidOperationException("无法启动 .NET SDK。请确认已安装 .NET 10 SDK。");
            var standardOutputTask = buildProcess.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardErrorTask = buildProcess.StandardError.ReadToEndAsync(cancellationToken);
            await buildProcess.WaitForExitAsync(cancellationToken);
            var buildOutput = (await standardOutputTask) + Environment.NewLine + (await standardErrorTask);
            if (buildProcess.ExitCode != 0)
            {
                TryDeleteDirectory(runtimeRoot);
                return new ToolRunResult(false, FormatBuildFailure(buildOutput), null);
            }

            var runtimeAssembly = Path.Combine(outputRoot, assemblyName + ".dll");
            if (!File.Exists(runtimeAssembly))
                throw new FileNotFoundException("编译成功，但没有找到工具运行程序集。", runtimeAssembly);

            var runInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = workspaceRoot,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            runInfo.ArgumentList.Add(runtimeAssembly);
            var runtimeProcess = Process.Start(runInfo)
                                 ?? throw new InvalidOperationException("工具运行进程启动失败。");
            _ = CleanupAfterExitAsync(runtimeProcess, runtimeRoot);
            return new ToolRunResult(true, "工具已完成编译并在独立窗口中运行。", runtimeProcess.Id);
        }
        catch (Exception exception)
        {
            TryDeleteDirectory(runtimeRoot);
            return new ToolRunResult(false, exception.Message, null);
        }
    }

    private static string CreateProjectFile(string workspaceRoot, string assemblyName)
    {
        var root = EscapeXml(Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var coreAssembly = EscapeXml(typeof(ToolPackageManifest).Assembly.Location);
        var clientCoreAssembly = EscapeXml(typeof(AppPath).Assembly.Location);
        return $$"""
                 <Project Sdk="Microsoft.NET.Sdk">
                   <PropertyGroup>
                     <OutputType>WinExe</OutputType>
                     <TargetFramework>net10.0-windows</TargetFramework>
                     <UseWPF>true</UseWPF>
                     <Nullable>enable</Nullable>
                     <ImplicitUsings>enable</ImplicitUsings>
                     <AssemblyName>{{assemblyName}}</AssemblyName>
                     <RootNamespace>XFEToolBox.RuntimeTool</RootNamespace>
                     <StartupObject>XFEToolBox.RuntimeHost.RuntimeEntry</StartupObject>
                     <EnableDefaultPageItems>false</EnableDefaultPageItems>
                     <EnableDefaultApplicationDefinition>false</EnableDefaultApplicationDefinition>
                   </PropertyGroup>
                   <ItemGroup>
                     <Compile Include="{{root}}\**\*.cs" Exclude="{{root}}\bin\**;{{root}}\obj\**" Link="Source\%(RecursiveDir)%(Filename)%(Extension)" />
                     <Page Include="{{root}}\**\*.xaml" Exclude="{{root}}\bin\**;{{root}}\obj\**" Link="Source\%(RecursiveDir)%(Filename)%(Extension)" />
                     <Resource Include="{{root}}\**\*.png;{{root}}\**\*.jpg;{{root}}\**\*.jpeg;{{root}}\**\*.gif;{{root}}\**\*.bmp;{{root}}\**\*.ico"
                               Exclude="{{root}}\bin\**;{{root}}\obj\**" Link="Source\%(RecursiveDir)%(Filename)%(Extension)" />
                   </ItemGroup>
                   <ItemGroup>
                     <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
                     <Reference Include="XFEToolBox.Core"><HintPath>{{coreAssembly}}</HintPath><Private>true</Private></Reference>
                     <Reference Include="XFEToolBox.Client.Core"><HintPath>{{clientCoreAssembly}}</HintPath><Private>true</Private></Reference>
                   </ItemGroup>
                 </Project>
                 """;
    }

    private static string CreateRuntimeEntry(ToolPackageManifest manifest)
    {
        var viewClass = JsonSerializer.Serialize(manifest.Entry.ViewClass);
        var title = JsonSerializer.Serialize($"{manifest.Name} · 运行预览");
        return $$"""
                 using System.Reflection;
                 using System.Windows;
                 using System.Windows.Media;

                 namespace XFEToolBox.RuntimeHost;

                 public static class RuntimeEntry
                 {
                     [STAThread]
                     public static void Main()
                     {
                         var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                         try
                         {
                             var viewType = Assembly.GetExecutingAssembly().GetType({{viewClass}}, throwOnError: true)!;
                             var instance = Activator.CreateInstance(viewType)
                                            ?? throw new InvalidOperationException("无法创建入口视图实例。");
                             if (instance is Window toolWindow)
                             {
                                 toolWindow.Title = {{title}};
                                 application.Run(toolWindow);
                                 return;
                             }

                             if (instance is not UIElement content)
                                 throw new InvalidOperationException("入口类型必须继承 UIElement 或 Window。");
                             var window = new Window
                             {
                                 Title = {{title}}, Width = 980, Height = 700, MinWidth = 560, MinHeight = 420,
                                 WindowStartupLocation = WindowStartupLocation.CenterScreen,
                                 Background = Brushes.White, Content = content
                             };
                             application.Run(window);
                         }
                         catch (Exception exception)
                         {
                             MessageBox.Show(exception.ToString(), "工具运行失败", MessageBoxButton.OK, MessageBoxImage.Error);
                         }
                     }
                 }
                 """;
    }

    private static async Task CleanupAfterExitAsync(Process process, string runtimeRoot)
    {
        try
        {
            await process.WaitForExitAsync();
            process.Dispose();
        }
        catch
        {
            // 运行进程已由系统结束时，无需继续等待。
        }
        finally
        {
            TryDeleteDirectory(runtimeRoot);
        }
    }

    private static string FormatBuildFailure(string output)
    {
        var importantLines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("error", StringComparison.OrdinalIgnoreCase)
                           || line.Contains("错误", StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToArray();
        if (importantLines.Length == 0)
            importantLines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).TakeLast(6).ToArray();
        return "工具编译未通过：\n" + string.Join(Environment.NewLine, importantLines);
    }

    private static string EscapeXml(string value) => SecurityElement.Escape(value) ?? value;

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // 运行进程退出瞬间仍可能占用文件，系统临时目录会在后续清理。
        }
    }
}

internal sealed record ToolRunResult(bool Success, string Message, int? ProcessId);
