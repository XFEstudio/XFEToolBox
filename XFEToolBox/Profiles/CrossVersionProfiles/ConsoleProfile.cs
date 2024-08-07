using XFEExtension.NetCore.AutoConfig;
using XFEToolBox.Core.Model;

namespace XFEToolBox.Profiles.CrossVersionProfiles;

public partial class ConsoleProfile
{
    [ProfileProperty]
    private int consolePort = 3280;
    [ProfileProperty]
    private string consolePassword = "";
    [ProfileProperty]
    private bool localOnly = true;
    public ConsoleProfile() => ProfilePath = @$"{AppPath.LocalProfile}\{typeof(ConsoleProfile)}.xfe";
}
