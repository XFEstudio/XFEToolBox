using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace XFEToolBox.Client.Wpf.Test;

public static class ChatPageXamlTests
{
    [Test]
    public static void InlineReadOnlyTextBindingsAreExplicitlyOneWay()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "XFEToolBox", "Views", "Pages", "ChatPage.xaml");
        var document = XDocument.Load(xamlPath, LoadOptions.SetLineInfo);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var bindings = document
            .Descendants(presentation + "Run")
            .Select(run => run.Attribute("Text"))
            .Where(attribute => attribute?.Value.StartsWith("{Binding", StringComparison.Ordinal) == true)
            .ToArray();

        Ensure(bindings.Length >= 6, "聊天页面的动态 Run.Text 绑定数量异常，回归测试可能已失效。");
        foreach (var binding in bindings)
        {
            Ensure(binding!.Value.Contains("Mode=OneWay", StringComparison.Ordinal),
                $"只读内联文本绑定没有显式使用 OneWay：{binding.Value}（第 {((IXmlLineInfo)binding).LineNumber} 行）。");
        }
    }

    [Test]
    public static void ChatUsesUnifiedNavigationAndWindowsNotificationLayout()
    {
        var repositoryRoot = FindRepositoryRoot();
        var chatPath = Path.Combine(repositoryRoot, "XFEToolBox", "Views", "Pages", "ChatPage.xaml");
        var settingPath = Path.Combine(repositoryRoot, "XFEToolBox", "Views", "Pages", "SettingPage.xaml");
        var chatText = File.ReadAllText(chatPath);
        var settingText = File.ReadAllText(settingPath);
        var document = XDocument.Load(chatPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        Ensure(!document.Descendants(presentation + "InfoBar").Any(), "聊天页仍显示顶部 InfoBar。 ");
        Ensure(chatText.Contains("ItemsSource=\"{Binding NavigationItems}\"", StringComparison.Ordinal),
            "聊天页没有绑定好友与群聊统一列表。 ");
        Ensure(chatText.Contains("Command=\"{Binding OpenLobbyCommand}\"", StringComparison.Ordinal),
            "统一列表顶部缺少大厅入口。 ");
        Ensure(chatText.Contains("IsChecked=\"{Binding ManagedGroupIsMuted}\"", StringComparison.Ordinal),
            "群聊管理缺少消息免打扰设置。 ");
        Ensure(!chatText.Contains("Content=\"☎\"", StringComparison.Ordinal) &&
               document.Descendants(presentation + "Path").Any(path => path.Attribute("Data")?.Value.Contains("M6.62,10.79", StringComparison.Ordinal) == true),
            "语音按钮没有使用简约电话路径图标。 ");
        Ensure(settingText.Contains("SystemProfile.ChatSinglePaneMode", StringComparison.Ordinal),
            "设置页缺少聊天单栏模式开关。 ");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "XFEToolBox.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 XFEToolBox 仓库根目录。");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
