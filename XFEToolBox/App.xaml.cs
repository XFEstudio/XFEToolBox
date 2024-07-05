using System.Windows;
using XFEExtension.NetCore.AutoConfig;
using XFEToolBox.Core.Model;
using XFEToolBox.Profiles;

namespace XFEToolBox;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        foreach (var profile in XFEProfile.Profiles)
        {
            //TODO:1 XFE各类拓展的AutoConfig需要改进
        }
    }
}
