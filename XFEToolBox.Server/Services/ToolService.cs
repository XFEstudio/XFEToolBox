using XFEExtension.NetCore.ServerInteractive.Attributes;
using XFEExtension.NetCore.ServerInteractive.Implements.CoreService;

namespace XFEToolBox.Server.Services;

public partial class ToolService : ServerCoreStandardServiceBase
{
    [EntryPoint("tool/get/list")]
    public async Task GetToolListEntryPoint()
    {

    }
}
