using XFEToolBox.Core.Model;

namespace XFEToolBox.Profiles.CrossVersionProfiles;

public partial class SystemProfile
{
    /// <summary>
    /// 工具箱现在是否可以被关闭
    /// </summary>
    public static bool CanClosed { get; set; }
    //public SystemProfile() => ProfilePath = @$"{AppPath.LocalProfile}\{typeof(SystemProfile)}.xfe";
}
