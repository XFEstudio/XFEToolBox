using CommunityToolkit.Mvvm.ComponentModel;

namespace XFEToolBox.Tools.SpaceEngineers;

public sealed class ManagerSettings
{
    public string Bin64Path { get; set; } = "";
    public string LauncherPath { get; set; } = "";
    public string BridgePath { get; set; } = "";
    public string ProfilePath { get; set; } = "";
    public List<string> Arguments { get; set; } = [];
    public List<string> KnownLauncherPaths { get; set; } = [];
    public bool AutoRestartAfterApply { get; set; }
    public bool RestartOnLocalChanges { get; set; }
    public int CloseTimeoutSeconds { get; set; } = 120;
}

public enum PluginCategory { Local }
public sealed class PluginRow : ObservableObject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string Description { get; set; } = "";
    public PluginCategory Category { get; set; }
    private bool isEnabled;
    public bool IsEnabled { get => isEnabled; set => SetProperty(ref isEnabled, value); }
    public string CategoryName => "XFE 插件";
}

public sealed class ManagerState
{
    public bool IsRunning { get; init; }
    public string ProcessStatus { get; init; } = "游戏未运行";
    public string LoaderStatus { get; init; } = "尚未构建 XFE 加载器";
    public IReadOnlyList<PluginRow> Plugins { get; init; } = [];
}
