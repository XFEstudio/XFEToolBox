using XFEToolBox.Core.Model;

namespace XFEToolBox.Test;

public class Program
{
    [SMTest]
    public static void PathTest()
    {
        Console.WriteLine($"""
            应用同步文件夹：{AppPath.AppSynData}
            应用本地文件夹：{AppPath.AppLocalData}
            应用本地版本文件夹：{AppPath.AppLocalVersionData}
            应用缓存文件夹：{AppPath.AppCache}
            """);
    }
}