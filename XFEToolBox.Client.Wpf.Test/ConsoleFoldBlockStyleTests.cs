using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using XFEToolBox.Client.Utilities;

namespace XFEToolBox.Client.Wpf.Test;

public static class ConsoleFoldBlockStyleTests
{
    [Test]
    public static void FoldBlockUsesCompactDedicatedButtonAndTogglesItsState()
    {
        RunSta(() =>
        {
            var converterType = typeof(BufferedConsoleRenderer).Assembly.GetType(
                "XFEToolBox.Client.Utilities.DecoratedTextConverter",
                throwOnError: true)!;
            var convertMethod = converterType.GetMethod(
                "ConvertToInlineList",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(string), typeof(Color)],
                modifiers: null)!;
            const string markup =
                "[foldblock color: white #9898e7 title: 分析对象：ConsoleShowcaseObject text: 第一行\n第二行]";
            var converted = (IEnumerable)convertMethod.Invoke(null, [markup, Colors.White])!;
            var foldGrid = converted.Cast<object>().OfType<Grid>().Single();

            Ensure(foldGrid.ColumnDefinitions.Count == 3, "折叠块标题没有使用独立的按钮列。");
            Ensure(Math.Abs(foldGrid.ColumnDefinitions[1].Width.Value - 34) < double.Epsilon,
                "折叠按钮列没有保持紧凑宽度。");

            var titleBorder = (Border)foldGrid.Children[0];
            var buttonBorder = (Border)foldGrid.Children[1];
            var contentBorder = (Border)foldGrid.Children[2];
            var button = (Button)buttonBorder.Child;
            var chevron = button.Content as Path;

            Ensure(button.Style is not null, "折叠按钮没有使用专用样式。");
            Ensure(button.MinWidth == 0 && Math.Abs(button.Width - 34) < double.Epsilon,
                "折叠按钮仍受全局 Button 最小宽度影响。");
            Ensure(button.Padding == new Thickness(0), "折叠按钮仍保留了全局 Button 内边距。");
            Ensure(chevron?.RenderTransform is RotateTransform,
                "折叠按钮没有使用可旋转的 Fluent 折线图标。");
            Ensure(GetBrushColor(titleBorder.Background) == Color.FromRgb(0x98, 0x98, 0xE7),
                "折叠块标题背景色不正确。");
            Ensure(GetBrushColor(buttonBorder.Background) == GetBrushColor(titleBorder.Background),
                "折叠按钮与标题没有形成统一色块。");
            Ensure(contentBorder.Visibility == Visibility.Collapsed && Equals(button.ToolTip, "展开详情"),
                "折叠块初始状态不正确。");

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Ensure(contentBorder.Visibility == Visibility.Visible && Equals(button.ToolTip, "折叠详情"),
                "点击后折叠块没有展开。");
            Ensure(Math.Abs(((RotateTransform)chevron!.RenderTransform).Angle - 180) < double.Epsilon,
                "展开时折线图标没有旋转。");

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Ensure(contentBorder.Visibility == Visibility.Collapsed && Equals(button.ToolTip, "展开详情"),
                "再次点击后折叠块没有收起。");
            Ensure(Math.Abs(((RotateTransform)chevron.RenderTransform).Angle) < double.Epsilon,
                "收起时折线图标没有复位。");
        });
    }

    private static Color GetBrushColor(Brush brush)
        => brush is SolidColorBrush solidColorBrush
            ? solidColorBrush.Color
            : throw new InvalidOperationException("预期使用纯色画刷。");

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
        Ensure(thread.Join(TimeSpan.FromSeconds(10)), "折叠块 WPF 回归测试超时。");
        if (failure is not null)
            throw new InvalidOperationException(failure.Message, failure);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
