using XFEToolBox.Client.Models.Chat;
using XFEToolBox.Client.Utilities.Chat;
using XFEToolBox.Client.ViewModel.Chat;
using XFEToolBox.Core.Chat;

namespace XFEToolBox.Client.Wpf.Test;

public static class ChatNavigationTests
{
    [Test]
    public static void NavigationItemsExposeLatestMessageAndMutedStateSafely()
    {
        var now = DateTimeOffset.UtcNow;
        var friend = new ChatFriendInfo
        {
            User = new ChatUserSummary { Id = "friend", UserName = "friend-account", NickName = "好友" },
            ConversationId = "direct"
        };
        var conversation = new ChatConversationInfo
        {
            Id = "direct",
            Kind = ChatConversationKind.Direct,
            Friend = friend.User,
            LastMessage = new ChatMessageInfo
            {
                Id = "message",
                ConversationId = "direct",
                Sender = friend.User,
                Text = "最新消息",
                CreatedAtUtc = now
            }
        };
        var item = new ChatNavigationItem(conversation, friend);

        Ensure(item.Key == "friend:friend", "好友统一列表键不稳定。 ");
        Ensure(item.Subtitle == "最新消息" && item.LastMessageAtUtc == now, "统一列表没有使用最后一条消息。 ");
        Ensure(!item.IsGroup && !item.IsMuted, "好友被错误识别成免打扰群聊。 ");
        Ensure(ChatNotificationPreferences.ParseMutedGroupIds("[\"g1\",\"g1\",\"\"]").SetEquals(["g1"]),
            "群聊免打扰配置没有去重或过滤空值。 ");
        Ensure(ChatNotificationPreferences.ParseMutedGroupIds("{损坏").Count == 0,
            "损坏的群聊免打扰配置没有安全恢复。 ");
    }

    [Test]
    public static void SinglePaneModeSwitchesBetweenListAndDetail()
    {
        var viewModel = new ChatPageViewModel { IsSinglePaneMode = true };
        Ensure(viewModel.ShowListPane && !viewModel.ShowDetailPane, "单栏模式初始状态没有只显示列表。 ");

        viewModel.OpenLobbyCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Ensure(!viewModel.ShowListPane && viewModel.ShowDetailPane && viewModel.ShowBackButton,
            "打开大厅后单栏模式没有切换到详情。 ");

        viewModel.CloseDetailCommand.Execute(null);
        Ensure(viewModel.ShowListPane && !viewModel.ShowDetailPane && !viewModel.ShowBackButton,
            "返回后单栏模式没有恢复会话列表。 ");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
