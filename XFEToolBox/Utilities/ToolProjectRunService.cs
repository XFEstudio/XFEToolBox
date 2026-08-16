using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XFEToolBox.Core.Model;
using XFEToolBox.Core.Tools;

namespace XFEToolBox.Client.Utilities;

internal static class ToolProjectRunService
{
    private const int MaximumPackageEntryCount = 512;
    private const long MaximumExtractedPackageBytes = 128L * 1024 * 1024;
    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<ToolRunResult> BuildAsync(
        string workspaceRoot,
        ToolPackageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        return await BuildAndRunCoreAsync(
            workspaceRoot,
            manifest,
            $"{manifest.Name} · 生成验证",
            temporaryWorkspaceRoot: null,
            launchAfterBuild: false,
            cancellationToken);
    }

    public static async Task<ToolRunResult> BuildAndRunAsync(
        string workspaceRoot,
        ToolPackageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        return await BuildAndRunCoreAsync(
            workspaceRoot,
            manifest,
            $"{manifest.Name} · 运行预览",
            temporaryWorkspaceRoot: null,
            launchAfterBuild: true,
            cancellationToken);
    }

    public static async Task<ToolRunResult> BuildPackageAndRunAsync(
        string packagePath,
        string expectedToolId,
        string expectedVersion,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        var packageWorkspaceRoot = Path.Combine(
            Path.GetTempPath(),
            "XFEToolBox",
            "PackageRuns",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(packageWorkspaceRoot);

        try
        {
            await VerifyPackageHashAsync(packagePath, expectedSha256, cancellationToken);
            var manifest = await ExtractAndValidatePackageAsync(
                packagePath,
                packageWorkspaceRoot,
                expectedToolId,
                expectedVersion,
                cancellationToken);
            return await BuildAndRunCoreAsync(
                packageWorkspaceRoot,
                manifest,
                manifest.Name,
                packageWorkspaceRoot,
                launchAfterBuild: true,
                cancellationToken);
        }
        catch (Exception exception)
        {
            TryDeleteDirectory(packageWorkspaceRoot);
            return new ToolRunResult(false, exception.Message, null);
        }
    }

    private static async Task<ToolRunResult> BuildAndRunCoreAsync(
        string workspaceRoot,
        ToolPackageManifest manifest,
        string windowTitle,
        string? temporaryWorkspaceRoot,
        bool launchAfterBuild,
        CancellationToken cancellationToken)
    {
        var runtimeRoot = Path.Combine(Path.GetTempPath(), "XFEToolBox", "CodeStudioRuns", Guid.NewGuid().ToString("N"));
        var outputRoot = Path.Combine(runtimeRoot, "output");
        Directory.CreateDirectory(runtimeRoot);

        try
        {
            var assemblyName = $"XFEToolRuntime_{Guid.NewGuid():N}";
            var hostAssemblyName = typeof(ToolProjectRunService).Assembly.GetName().Name
                                   ?? throw new InvalidOperationException("无法确定宿主程序集名称。");
            var preparedWorkspaceRoot = Path.Combine(runtimeRoot, "source");
            await PrepareWorkspaceAsync(
                workspaceRoot,
                preparedWorkspaceRoot,
                hostAssemblyName,
                cancellationToken);
            var projectPath = Path.Combine(runtimeRoot, "ToolRuntime.csproj");
            var entryPath = Path.Combine(runtimeRoot, "RuntimeEntry.g.cs");
            var toolIconPath = ResolveToolIconPath(preparedWorkspaceRoot, manifest.Icon);
            await File.WriteAllTextAsync(projectPath, CreateProjectFile(preparedWorkspaceRoot, assemblyName, hostAssemblyName), new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(entryPath, CreateRuntimeEntry(manifest, hostAssemblyName, windowTitle, toolIconPath), new UTF8Encoding(false), cancellationToken);

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
                if (temporaryWorkspaceRoot is not null)
                    TryDeleteDirectory(temporaryWorkspaceRoot);
                return new ToolRunResult(false, FormatBuildFailure(buildOutput), null);
            }

            var runtimeAssembly = Path.Combine(outputRoot, assemblyName + ".dll");
            if (!File.Exists(runtimeAssembly))
                throw new FileNotFoundException("编译成功，但没有找到工具运行程序集。", runtimeAssembly);

            if (!launchAfterBuild)
            {
                TryDeleteDirectory(runtimeRoot);
                if (temporaryWorkspaceRoot is not null)
                    TryDeleteDirectory(temporaryWorkspaceRoot);
                return new ToolRunResult(true, "工具工程已成功生成。", null);
            }

            var runInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = workspaceRoot,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            runInfo.ArgumentList.Add(runtimeAssembly);
            var runtimeProcess = Process.Start(runInfo)
                                 ?? throw new InvalidOperationException("工具运行进程启动失败。");
            _ = CleanupAfterExitAsync(runtimeProcess, runtimeRoot, temporaryWorkspaceRoot);
            return new ToolRunResult(true, "工具已完成编译并在独立窗口中运行。", runtimeProcess.Id);
        }
        catch (Exception exception)
        {
            TryDeleteDirectory(runtimeRoot);
            if (temporaryWorkspaceRoot is not null)
                TryDeleteDirectory(temporaryWorkspaceRoot);
            return new ToolRunResult(false, exception.Message, null);
        }
    }

    private static string CreateProjectFile(string workspaceRoot, string assemblyName, string hostAssemblyName)
    {
        var root = EscapeXml(Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var coreAssembly = EscapeXml(typeof(ToolPackageManifest).Assembly.Location);
        var clientCoreAssembly = EscapeXml(typeof(AppPath).Assembly.Location);
        var clientAssemblyPath = typeof(ToolProjectRunService).Assembly.Location;
        var clientAssembly = EscapeXml(clientAssemblyPath);
        var xfeExtensionAssembly = EscapeXml(Path.Combine(
            Path.GetDirectoryName(clientAssemblyPath)!,
            "XFEExtension.NetCore.dll"));
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
                     <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                     <EnableDefaultPageItems>false</EnableDefaultPageItems>
                     <EnableDefaultApplicationDefinition>false</EnableDefaultApplicationDefinition>
                   </PropertyGroup>
                   <ItemGroup>
                     <Compile Include="RuntimeEntry.g.cs" />
                     <Compile Include="{{root}}\**\*.cs" Exclude="{{root}}\bin\**;{{root}}\obj\**" Link="Source\%(RecursiveDir)%(Filename)%(Extension)" />
                     <Page Include="{{root}}\**\*.xaml" Exclude="{{root}}\bin\**;{{root}}\obj\**" Link="Source\%(RecursiveDir)%(Filename)%(Extension)" />
                     <Resource Include="{{root}}\**\*.png;{{root}}\**\*.jpg;{{root}}\**\*.jpeg;{{root}}\**\*.gif;{{root}}\**\*.bmp;{{root}}\**\*.ico"
                               Exclude="{{root}}\bin\**;{{root}}\obj\**" Link="Source\%(RecursiveDir)%(Filename)%(Extension)" />
                   </ItemGroup>
                   <ItemGroup>
                     <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
                     <Reference Include="XFEToolBox.Core"><HintPath>{{coreAssembly}}</HintPath><Private>true</Private></Reference>
                     <Reference Include="XFEToolBox.Client.Core"><HintPath>{{clientCoreAssembly}}</HintPath><Private>true</Private></Reference>
                     <Reference Include="XFEExtension.NetCore"><HintPath>{{xfeExtensionAssembly}}</HintPath><Private>true</Private></Reference>
                     <Reference Include="{{EscapeXml(hostAssemblyName)}}"><HintPath>{{clientAssembly}}</HintPath><Private>true</Private></Reference>
                   </ItemGroup>
                 </Project>
                 """;
    }

    private static string CreateRuntimeEntry(
        ToolPackageManifest manifest,
        string hostAssemblyName,
        string windowTitle,
        string? toolIconPath)
    {
        var window = NormalizeWindowSettings(manifest.Window);
        var toolId = JsonSerializer.Serialize(manifest.Id.Trim());
        var viewClass = JsonSerializer.Serialize(manifest.Entry.ViewClass);
        var title = JsonSerializer.Serialize(windowTitle);
        var displayTitle = JsonSerializer.Serialize(manifest.Name.Trim());
        var subtitle = JsonSerializer.Serialize(
            string.IsNullOrWhiteSpace(manifest.Subtitle)
                ? manifest.Description.Trim()
                : manifest.Subtitle.Trim());
        var iconPath = JsonSerializer.Serialize(toolIconPath);
        var themeResourceUri = JsonSerializer.Serialize(
            $"pack://application:,,,/{hostAssemblyName};component/Resources/Style/ToolThemeResources.xaml");
        var mainStyleResourceUri = JsonSerializer.Serialize(
            $"pack://application:,,,/{hostAssemblyName};component/Resources/Style/MainStyle.xaml");
        var defaultIconResourceUri = JsonSerializer.Serialize(
            $"pack://application:,,,/{hostAssemblyName};component/Resources/Image/default_tool_icon.png");
        var width = JsonSerializer.Serialize(window.Width);
        var height = JsonSerializer.Serialize(window.Height);
        var minWidth = JsonSerializer.Serialize(window.MinWidth);
        var minHeight = JsonSerializer.Serialize(window.MinHeight);
        var allowResize = JsonSerializer.Serialize(window.AllowResize);
        var allowMaximize = JsonSerializer.Serialize(window.AllowMaximize);
        var showMinimizeButton = JsonSerializer.Serialize(window.ShowMinimizeButton);
        var showCloseButton = JsonSerializer.Serialize(window.ShowCloseButton);
        return $$"""
                 using System.IO;
                 using System.Reflection;
                 using System.Windows;
                 using System.Windows.Controls;
                 using System.Windows.Media;
                 using System.Windows.Media.Imaging;
                 using System.Windows.Threading;
                 using XFEToolBox.Client.Views.Controls;
                 using XFEToolBox.Core.Tools;

                 namespace XFEToolBox.RuntimeHost;

                 public static class RuntimeEntry
                 {
                     [STAThread]
                     public static void Main()
                     {
                         var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                         application.Resources.MergedDictionaries.Add(new ResourceDictionary
                         {
                             Source = new Uri({{themeResourceUri}}, UriKind.Absolute)
                         });
                         application.Resources.MergedDictionaries.Add(new ResourceDictionary
                         {
                             Source = new Uri({{mainStyleResourceUri}}, UriKind.Absolute)
                         });
                         try
                         {
                             ToolDataStore.Initialize({{toolId}});
                             var viewType = Assembly.GetExecutingAssembly().GetType({{viewClass}}, throwOnError: true)!;
                             var instance = Activator.CreateInstance(viewType)
                                            ?? throw new InvalidOperationException("无法创建入口视图实例。");
                             Window window;
                             object content;
                             if (instance is Window toolWindow)
                             {
                                 window = toolWindow;
                                 content = toolWindow.Content ?? new Grid();
                                 toolWindow.Content = null;
                             }
                             else
                             {
                                 if (instance is not UIElement toolContent)
                                     throw new InvalidOperationException("入口类型必须继承 UIElement 或 Window。");
                                 window = new Window();
                                 content = toolContent;
                             }

                             ConfigureWindow(application, window, content);
                             application.Run(window);
                         }
                         catch (Exception exception)
                         {
                             MessageBox.Show(exception.ToString(), "工具运行失败", MessageBoxButton.OK, MessageBoxImage.Error);
                         }
                     }

                     private static void ConfigureWindow(Application application, Window window, object content)
                     {
                         var savedPlacement = ToolDataStore.ReadWindowPlacement();
                         window.Title = {{title}};
                         window.MinWidth = {{minWidth}};
                         window.MinHeight = {{minHeight}};
                         window.Width = NormalizePlacementDimension(savedPlacement?.Width, {{width}}, window.MinWidth);
                         window.Height = NormalizePlacementDimension(savedPlacement?.Height, {{height}}, window.MinHeight);
                         window.SizeToContent = SizeToContent.Manual;
                         window.WindowState = WindowState.Normal;
                         if (savedPlacement is not null && IsPlacementVisible(savedPlacement))
                         {
                             window.WindowStartupLocation = WindowStartupLocation.Manual;
                             window.Left = savedPlacement.Left;
                             window.Top = savedPlacement.Top;
                         }
                         else
                         {
                             window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                         }
                         window.WindowStyle = WindowStyle.None;
                         window.AllowsTransparency = true;
                         window.ResizeMode = {{allowResize}} ? ResizeMode.CanResize : ResizeMode.NoResize;
                         window.Background = Brushes.Transparent;
                         window.Foreground = (Brush)application.FindResource("ToolTextPrimaryBrush");
                         var windowIcon = LoadWindowIcon();
                         window.Icon = windowIcon;

                         var captionBar = new WindowCaptionBar
                         {
                             Height = 25,
                             VerticalAlignment = VerticalAlignment.Top,
                             AllowMaximize = {{allowResize}} && {{allowMaximize}},
                             DragHandleVisibility = Visibility.Visible,
                             MinimizeButtonVisibility = {{showMinimizeButton}} ? Visibility.Visible : Visibility.Collapsed,
                             CloseButtonVisibility = {{showCloseButton}} ? Visibility.Visible : Visibility.Collapsed
                         };
                         Grid.SetRow(captionBar, 0);

                         var titleIcon = new Image
                         {
                             Source = windowIcon,
                             Width = 28,
                             Height = 28,
                             Stretch = Stretch.Uniform,
                             HorizontalAlignment = HorizontalAlignment.Center,
                             VerticalAlignment = VerticalAlignment.Center
                         };
                         var titleIconSurface = new Border
                         {
                             Width = 42,
                             Height = 42,
                             CornerRadius = new CornerRadius(12),
                             Background = Brushes.White,
                             Child = titleIcon
                         };
                         var titleText = new TextBlock
                         {
                             Text = {{displayTitle}},
                             Foreground = Brushes.White,
                             FontSize = 16,
                             FontWeight = FontWeights.SemiBold,
                             TextTrimming = TextTrimming.CharacterEllipsis
                         };
                         var subtitleText = new TextBlock
                         {
                             Text = {{subtitle}},
                             Foreground = Brushes.White,
                             FontSize = 10.5,
                             Opacity = 0.82,
                             Margin = new Thickness(0, 2, 0, 0),
                             TextTrimming = TextTrimming.CharacterEllipsis
                         };
                         var titleTextPanel = new StackPanel
                         {
                             Margin = new Thickness(11, 0, 0, 0),
                             VerticalAlignment = VerticalAlignment.Center
                         };
                         titleTextPanel.Children.Add(titleText);
                         titleTextPanel.Children.Add(subtitleText);

                         var titleIdentity = new Grid
                         {
                             Margin = new Thickness(18, 9, 100, 9),
                             HorizontalAlignment = HorizontalAlignment.Stretch,
                             VerticalAlignment = VerticalAlignment.Center,
                             IsHitTestVisible = false
                         };
                         titleIdentity.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                         titleIdentity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                         Grid.SetColumn(titleIconSurface, 0);
                         Grid.SetColumn(titleTextPanel, 1);
                         titleIdentity.Children.Add(titleIconSurface);
                         titleIdentity.Children.Add(titleTextPanel);
                         Grid.SetRow(titleIdentity, 0);

                         var contentPresenter = new ContentControl
                         {
                             Content = content,
                             HorizontalContentAlignment = HorizontalAlignment.Stretch,
                             VerticalContentAlignment = VerticalAlignment.Stretch
                         };
                         var contentSurface = new Border
                         {
                             Background = (Brush)application.FindResource("ToolSurfaceBrush"),
                             CornerRadius = new CornerRadius(16, 16, 17, 17),
                             Child = contentPresenter
                         };
                         Grid.SetRow(contentSurface, 1);

                         var layout = new Grid();
                         layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
                         layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                         layout.Children.Add(captionBar);
                         layout.Children.Add(titleIdentity);
                         layout.Children.Add(contentSurface);

                         var windowSurface = new RoundedClipBorder
                         {
                             Margin = new Thickness(5),
                             CornerRadius = new CornerRadius(19),
                             Background = (Brush)application.FindResource("MainColor"),
                             Child = layout
                         };
                         var windowRoot = new Grid();
                         windowRoot.Children.Add(windowSurface);
                         if ({{allowResize}})
                         {
                             windowRoot.Children.Add(new WindowResizeGrip
                             {
                                 Margin = new Thickness(0, 0, 5, 5)
                             });
                         }
                         window.Content = windowRoot;
                         AttachWindowPlacementPersistence(window, savedPlacement);
                     }

                     private static double NormalizePlacementDimension(double? value, double fallback, double minimum)
                     {
                         if (value is not { } candidate || !double.IsFinite(candidate) || candidate <= 0)
                             return fallback;
                         return Math.Max(minimum, candidate);
                     }

                     private static bool IsPlacementVisible(ToolWindowPlacement placement)
                     {
                         if (!double.IsFinite(placement.Left) || !double.IsFinite(placement.Top) ||
                             !double.IsFinite(placement.Width) || !double.IsFinite(placement.Height) ||
                             placement.Width <= 0 || placement.Height <= 0)
                             return false;

                         const double visibleEdge = 72;
                         var virtualLeft = SystemParameters.VirtualScreenLeft;
                         var virtualTop = SystemParameters.VirtualScreenTop;
                         var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
                         var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
                         return placement.Left + placement.Width >= virtualLeft + visibleEdge &&
                                placement.Top + visibleEdge <= virtualBottom &&
                                placement.Left + visibleEdge <= virtualRight &&
                                placement.Top + placement.Height >= virtualTop + visibleEdge;
                     }

                     private static void AttachWindowPlacementPersistence(Window window, ToolWindowPlacement? savedPlacement)
                     {
                         var lastVisibleState = string.Equals(
                             savedPlacement?.LastVisibleState,
                             nameof(WindowState.Maximized),
                             StringComparison.Ordinal)
                                 ? WindowState.Maximized
                                 : WindowState.Normal;
                         var saveTimer = new DispatcherTimer(DispatcherPriority.Background)
                         {
                             Interval = TimeSpan.FromMilliseconds(350)
                         };

                         void SavePlacement()
                         {
                             saveTimer.Stop();
                             var state = window.WindowState;
                             if (state != WindowState.Minimized)
                                 lastVisibleState = state == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;

                             var bounds = state == WindowState.Normal
                                 ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
                                 : window.RestoreBounds;
                             if (!double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top) ||
                                 !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) ||
                                 bounds.Width <= 0 || bounds.Height <= 0)
                                 return;

                             ToolDataStore.WriteWindowPlacement(new ToolWindowPlacement(
                                 bounds.Left,
                                 bounds.Top,
                                 bounds.Width,
                                 bounds.Height,
                                 state.ToString(),
                                 lastVisibleState.ToString(),
                                 state == WindowState.Minimized,
                                 DateTimeOffset.UtcNow));
                         }

                         void QueueSave()
                         {
                             saveTimer.Stop();
                             saveTimer.Start();
                         }

                         saveTimer.Tick += (_, _) => SavePlacement();
                         window.SizeChanged += (_, _) => QueueSave();
                         window.LocationChanged += (_, _) => QueueSave();
                         window.StateChanged += (_, _) => QueueSave();
                         window.Closing += (_, _) => SavePlacement();
                         window.Loaded += (_, _) =>
                         {
                             // 记录最小化状态，但启动时恢复到最后一个可见状态，避免用户误以为工具没有打开。
                             var restoreMaximized = {{allowResize}} && {{allowMaximize}} &&
                                 (string.Equals(savedPlacement?.State, nameof(WindowState.Maximized), StringComparison.Ordinal) ||
                                  string.Equals(savedPlacement?.State, nameof(WindowState.Minimized), StringComparison.Ordinal) &&
                                  string.Equals(savedPlacement?.LastVisibleState, nameof(WindowState.Maximized), StringComparison.Ordinal));
                             if (restoreMaximized)
                                 window.WindowState = WindowState.Maximized;
                         };
                     }

                     private static ImageSource LoadWindowIcon()
                     {
                         try
                         {
                             string? path = {{iconPath}};
                             if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                             {
                                 var image = new BitmapImage();
                                 image.BeginInit();
                                 image.CacheOption = BitmapCacheOption.OnLoad;
                                 image.UriSource = new Uri(path, UriKind.Absolute);
                                 image.EndInit();
                                 image.Freeze();
                                 return image;
                             }
                         }
                         catch
                         {
                             // 包内图标不可用时回退到宿主的默认工具图标。
                         }

                         return new BitmapImage(new Uri({{defaultIconResourceUri}}, UriKind.Absolute));
                     }
                 }
                 """;
    }

    private static NormalizedWindowSettings NormalizeWindowSettings(ToolWindowManifest? settings)
    {
        settings ??= new ToolWindowManifest();
        var minWidth = NormalizeDimension(settings.MinWidth, ToolWindowManifest.DefaultMinWidth, 320, 3840);
        var minHeight = NormalizeDimension(settings.MinHeight, ToolWindowManifest.DefaultMinHeight, 220, 2160);
        var width = Math.Max(minWidth, NormalizeDimension(settings.Width, ToolWindowManifest.DefaultWidth, 320, 3840));
        var height = Math.Max(minHeight, NormalizeDimension(settings.Height, ToolWindowManifest.DefaultHeight, 220, 2160));
        return new NormalizedWindowSettings(
            width,
            height,
            minWidth,
            minHeight,
            settings.AllowResize,
            settings.AllowResize && settings.AllowMaximize,
            settings.ShowMinimizeButton,
            settings.ShowCloseButton);
    }

    private static double NormalizeDimension(double value, double fallback, double minimum, double maximum) =>
        Math.Clamp(double.IsFinite(value) && value > 0 ? value : fallback, minimum, maximum);

    private static string? ResolveToolIconPath(string workspaceRoot, string? manifestIcon)
    {
        if (string.IsNullOrWhiteSpace(manifestIcon) || Path.IsPathRooted(manifestIcon))
            return null;

        var root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, manifestIcon.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            return null;
        return path;
    }

    private sealed record NormalizedWindowSettings(
        double Width,
        double Height,
        double MinWidth,
        double MinHeight,
        bool AllowResize,
        bool AllowMaximize,
        bool ShowMinimizeButton,
        bool ShowCloseButton);

    private static async Task CleanupAfterExitAsync(
        Process process,
        string runtimeRoot,
        string? temporaryWorkspaceRoot)
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
            if (temporaryWorkspaceRoot is not null)
                TryDeleteDirectory(temporaryWorkspaceRoot);
        }
    }

    private static async Task VerifyPackageHashAsync(
        string packagePath,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(packagePath))
            throw new FileNotFoundException("没有找到已缓存的工具包。", packagePath);
        if (string.IsNullOrWhiteSpace(expectedSha256))
            throw new InvalidDataException("服务器没有提供工具包 SHA-256。 ");

        await using var stream = new FileStream(
            packagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actualSha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("工具包校验失败，缓存内容可能已损坏。 ");
    }

    private static async Task PrepareWorkspaceAsync(
        string sourceRoot,
        string destinationRoot,
        string hostAssemblyName,
        CancellationToken cancellationToken)
    {
        var normalizedSourceRoot = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar)
                                   + Path.DirectorySeparatorChar;
        foreach (var sourcePath in Directory.EnumerateFiles(
                     normalizedSourceRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(normalizedSourceRoot, sourcePath);
            var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                                        || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                                        || segment.Equals(".git", StringComparison.OrdinalIgnoreCase)))
                continue;

            var extension = Path.GetExtension(sourcePath);
            if (!extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".ico", StringComparison.OrdinalIgnoreCase))
                continue;

            var destinationPath = Path.Combine(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            if (extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                var xaml = await File.ReadAllTextAsync(sourcePath, cancellationToken);
                xaml = NormalizeLegacyHostAssemblyReferences(xaml, hostAssemblyName);
                await File.WriteAllTextAsync(
                    destinationPath,
                    xaml,
                    new UTF8Encoding(false),
                    cancellationToken);
                continue;
            }

            await using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(destination, cancellationToken);
        }
    }

    private static string NormalizeLegacyHostAssemblyReferences(string xaml, string hostAssemblyName)
    {
        if (hostAssemblyName.Equals("XFEToolBox.Client", StringComparison.Ordinal))
            return xaml;

        return xaml
            .Replace(
                ";assembly=XFEToolBox.Client\"",
                $";assembly={hostAssemblyName}\"",
                StringComparison.Ordinal)
            .Replace(
                ";assembly=XFEToolBox.Client'",
                $";assembly={hostAssemblyName}'",
                StringComparison.Ordinal)
            .Replace(
                "/XFEToolBox.Client;component/",
                $"/{hostAssemblyName};component/",
                StringComparison.Ordinal);
    }

    private static async Task<ToolPackageManifest> ExtractAndValidatePackageAsync(
        string packagePath,
        string destinationRoot,
        string expectedToolId,
        string expectedVersion,
        CancellationToken cancellationToken)
    {
        var normalizedRoot = Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
        var extractedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long extractedBytes = 0;

        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count > MaximumPackageEntryCount)
            throw new InvalidDataException($"工具包文件数量超过限制（{MaximumPackageEntryCount}）。");

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = entry.FullName.Replace('\\', '/').TrimStart('/');
            var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                continue;
            if (Path.IsPathRooted(entry.FullName)
                || segments.Any(segment => segment is "." or ".." || segment.Contains(':')))
                throw new InvalidDataException($"工具包包含不安全路径：{entry.FullName}");

            var destinationPath = Path.GetFullPath(Path.Combine(
                normalizedRoot,
                Path.Combine(segments)));
            if (!destinationPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"工具包文件超出解包目录：{entry.FullName}");
            if (!extractedPaths.Add(destinationPath))
                throw new InvalidDataException($"工具包包含重复文件：{entry.FullName}");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            if (entry.Length < 0 || entry.Length > MaximumExtractedPackageBytes - extractedBytes)
                throw new InvalidDataException("工具包解压后的总大小超过 128 MB 限制。 ");
            extractedBytes += entry.Length;
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var source = entry.Open();
            await using var output = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(output, cancellationToken);
        }

        var manifestPath = Path.Combine(destinationRoot, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException("工具包缺少 manifest.json。 ");
        var manifest = JsonSerializer.Deserialize<ToolPackageManifest>(
                           await File.ReadAllTextAsync(manifestPath, cancellationToken),
                           ManifestJsonOptions)
                       ?? throw new InvalidDataException("manifest.json 内容为空。 ");
        if (manifest.PackageFormatVersion != ToolPackageManifest.CurrentPackageFormatVersion)
            throw new InvalidDataException($"不支持工具包格式版本 {manifest.PackageFormatVersion}。 ");
        if (!manifest.Id.Equals(expectedToolId, StringComparison.OrdinalIgnoreCase)
            || !manifest.Version.Equals(expectedVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("工具包清单与服务器目录信息不一致。 ");
        if (manifest.Entry is null)
            throw new InvalidDataException("工具包清单缺少入口配置。 ");

        EnsurePackageFileExists(normalizedRoot, manifest.Entry.ViewXaml, "入口 XAML");
        EnsurePackageFileExists(normalizedRoot, manifest.Entry.ViewCodeBehind, "入口代码后置");
        if (!string.IsNullOrWhiteSpace(manifest.Entry.ViewModel))
            EnsurePackageFileExists(normalizedRoot, manifest.Entry.ViewModel, "ViewModel");
        if (string.IsNullOrWhiteSpace(manifest.Entry.ViewClass))
            throw new InvalidDataException("工具包清单缺少入口视图类。 ");
        return manifest;
    }

    private static void EnsurePackageFileExists(string normalizedRoot, string relativePath, string description)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"工具包清单中的{description}路径无效。 ");
        var fullPath = Path.GetFullPath(Path.Combine(
            normalizedRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            throw new InvalidDataException($"工具包缺少{description}文件：{relativePath}");
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
