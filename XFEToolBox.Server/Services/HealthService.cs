using XFEExtension.NetCore.ServerInteractive.Attributes;
using XFEExtension.NetCore.ServerInteractive.Implements.CoreService;

namespace XFEToolBox.Server.Services;

public partial class HealthService : ServerCoreStandardServiceBase
{
    [EntryPoint("health")]
    public async Task HealthEntryPoint() => await Close(new
    {
        status = "healthy",
        utc = DateTimeOffset.UtcNow,
        packageFormatVersion = XFEToolBox.Core.Tools.ToolPackageManifest.CurrentPackageFormatVersion
    });
}
