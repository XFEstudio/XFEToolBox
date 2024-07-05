using System.Reflection;
using XFEExtension.NetCore.AutoPath;

namespace XFEToolBox.Core.Model;

public partial class AppPath
{
    [AutoPath]
    private static readonly string appSynData = @$"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\XFEToolBox";
    [AutoPath]
    private static readonly string appLocalData = @$"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\XFEToolBox\CrossVersion";
    [AutoPath]
    private static readonly string appLocalVersionData = @$"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\XFEToolBox\Versions\{(Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString() ?? "UnknownVersion")}";
    [AutoPath]
    private static readonly string appCache = @$"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\Temp\XFEToolBox";
    [AutoPath]
    private static readonly string localProfile = @$"{AppLocalData}\Profiles";
    [AutoPath]
    private static readonly string localVersionProfile = $@"{AppLocalVersionData}\Profiles";
    [AutoPath]
    private static readonly string synProfile = $@"{AppSynData}\Profiles";
}
