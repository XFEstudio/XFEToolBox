using System.Windows.Controls;
using XFEToolBox.Views.Windows;

namespace XFEToolBox.Utilities;

/// <summary>
/// 导航中心
/// </summary>
public static class NavigationCenter
{
    /// <summary>
    /// 导航堆栈
    /// </summary>
    public static List<Page> NavigationStack { get; set; } = [];
    private static bool canGoBack;
    /// <summary>
    /// 是否可以返回
    /// </summary>
    public static bool CanGoBack
    {
        get { return canGoBack; }
        set
        {
            if (MainWindow.Current is null)
                return;
            canGoBack = value;
            MainWindow.Current.backTabBorder.Visibility = canGoBack ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }
    }
    private static bool CheckCanGoBack() => NavigationStack.Count > 0 ? CanGoBack = true : CanGoBack = false;
    /// <summary>
    /// 返回上一个导航堆栈
    /// </summary>
    /// <param name="removeStack">是否从导航堆栈中移除</param>
    /// <returns>是否成功</returns>
    public static bool GoBack(bool removeStack = true)
    {
        if (MainWindow.Current is null)
            return false;
        MainWindow.Current.contentFrame.Content = NavigationStack[^2];
        if (removeStack && NavigationStack.Count > 0)
            NavigationStack.RemoveAt(NavigationStack.Count - 1);
        CheckCanGoBack();
        return true;
    }
    /// <summary>
    /// 导航至目标堆栈
    /// </summary>
    /// <param name="page">目标导航页面</param>
    /// <param name="addIntoStack">是否添加至导航堆栈中</param>
    /// <returns>是否成功</returns>
    public static bool Navigate(this Page page, bool addIntoStack = true)
    {
        if (MainWindow.Current is null)
            return false;
        MainWindow.Current.contentFrame.Content = NavigationStack[^1];
        if (addIntoStack)
            NavigationStack.Add(page);
        CheckCanGoBack();
        return true;
    }
}
