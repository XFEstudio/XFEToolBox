using System.Collections.ObjectModel;
using System.Collections.Specialized;
using XFEToolBox.Client.Models;
using XFEToolBox.Client.ViewModel.Pages;

namespace XFEToolBox.Client.Wpf.Test;

public static class HomeDashboardRefreshTests
{
    [Test]
    public static void ReturningHomeReusesCardsAndIconsWithoutResettingTheVisualTree() => RunSta(() =>
    {
        var cards = new ObservableCollection<LauncherItemViewModel>();
        MainPageViewModel.UpdateLauncherItems(cards, Enumerable.Range(0, 8).Select(index => Item(index)).ToArray());
        var originals = cards.ToArray();
        var icons = cards.Select(card => card.IconSource).ToArray();
        var iconTasks = cards.Select(card => card.IconLoadingTask).ToArray();
        var collectionChanges = 0;
        cards.CollectionChanged += (_, _) => collectionChanges++;
        var executedRevision = 0;

        for (var revision = 1; revision <= 250; revision++)
        {
            var currentRevision = revision;
            MainPageViewModel.UpdateLauncherItems(cards, Enumerable.Range(0, 8).Select(index => Item(index,
                execute: () => { executedRevision = currentRevision; return Task.CompletedTask; })).ToArray());
        }

        Ensure(collectionChanges == 0, $"重复打开主页触发了 {collectionChanges} 次未变化的卡片集合更新。");
        for (var index = 0; index < cards.Count; index++)
        {
            Ensure(ReferenceEquals(cards[index], originals[index]), "未变化的卡片没有复用。");
            Ensure(ReferenceEquals(cards[index].IconSource, icons[index]), "未变化的图标被重新创建。");
            Ensure(ReferenceEquals(cards[index].IconLoadingTask, iconTasks[index]), "未变化的图标触发了重新加载。");
        }
        cards[0].ExecuteAsync().GetAwaiter().GetResult();
        Ensure(executedRevision == 250, "复用卡片后执行了旧快照的启动逻辑。");
        Console.WriteLine("主页重复刷新 250 次：未变化卡片的集合变更 0 次，8 个图标均复用。");
    });

    [Test]
    public static void PinReorderingAndCatalogUpdatesStillRefreshTheAffectedCards() => RunSta(() =>
    {
        var cards = new ObservableCollection<LauncherItemViewModel>();
        MainPageViewModel.UpdateLauncherItems(cards, [Item(0), Item(1), Item(2)]);
        var originals = cards.ToArray();
        var pinNotifications = 0;
        originals[2].PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(LauncherItemViewModel.PinGlyph)) pinNotifications++;
        };
        var resetCount = 0;
        cards.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset) resetCount++;
        };

        MainPageViewModel.UpdateLauncherItems(cards, [Item(2, pinned: true), Item(0), Item(1)]);
        Ensure(ReferenceEquals(cards[0], originals[2]) && ReferenceEquals(cards[1], originals[0]),
            "固定项排序没有移动已有卡片。");
        Ensure(pinNotifications == 1, "固定项变化没有通知星标绑定。");

        MainPageViewModel.UpdateLauncherItems(cards, [Item(2, pinned: true), Item(0, title: "更新后的工具"), Item(3)]);
        Ensure(cards.Select(card => card.Item.TargetId).SequenceEqual(["home-test-2", "home-test-0", "home-test-3"]),
            "主页卡片没有同步新增、移除或排序变化。");
        Ensure(cards[1].Title == "更新后的工具" && !ReferenceEquals(cards[1], originals[0]), "目录修改没有更新对应卡片。");
        Ensure(ReferenceEquals(cards[0], originals[2]), "局部修改重建了无关卡片。");
        Ensure(resetCount == 0, "局部刷新仍然清空了整张卡片列表。");

        MainPageViewModel.UpdateLauncherItems(cards, []);
        Ensure(cards.Count == 0, "空目录没有清除已移除条目。");
    });

    [Test]
    public static void ActivityProgressDoesNotRecreateExistingActivityRows()
    {
        var item = new ActivityItem("home-activity", ActivityKind.General, "下载测试", false);
        var rows = new ObservableCollection<ActivityItem>();
        MainPageViewModel.SynchronizeItems(rows, [item]);
        var collectionChanges = 0;
        rows.CollectionChanged += (_, _) => collectionChanges++;

        for (var progress = 0; progress <= 100; progress++)
        {
            item.Progress = progress;
            MainPageViewModel.SynchronizeItems(rows, [item]);
        }

        Ensure(collectionChanges == 0 && ReferenceEquals(rows[0], item) && rows[0].Progress == 100,
            "活动进度变化重建了行或未保留进度绑定。");
        var next = new ActivityItem("next-activity", ActivityKind.General, "构建测试", false);
        MainPageViewModel.SynchronizeItems(rows, [next, item]);
        Ensure(rows.Count == 2 && ReferenceEquals(rows[1], item), "新增活动未保留已有行。");
    }

    private static LauncherItem Item(int index, string? title = null, bool pinned = false, Func<Task>? execute = null) => new()
    {
        Kind = LauncherItemKind.Tool,
        TargetId = $"home-test-{index}",
        Title = title ?? $"工具 {index}",
        IconReference = "/Resources/Image/default_tool_icon.png",
        IsPinned = pinned,
        ExecuteAsync = execute ?? (() => Task.CompletedTask)
    };

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Ensure(thread.Join(TimeSpan.FromSeconds(15)), "主页刷新回归测试超时。");
        if (failure is not null) throw new InvalidOperationException("主页刷新回归失败。", failure);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
