using System.Windows;
using System.Windows.Controls;
using XFEToolBox.WpfCore.Controls;

namespace XFEToolBox.Client.Wpf.Test;

public static class UnifiedControlDefaultsTests
{
    [Test]
    public static void DataGridGeneratedColumnsReceiveUnifiedStylesWithoutOverwritingExplicitStyles()
    {
        RunSta(() =>
        {
            var elementStyle = new Style(typeof(CheckBox));
            var editingStyle = new Style(typeof(CheckBox));
            var grid = new DataGrid();
            DataGridAssist.SetCheckBoxElementStyle(grid, elementStyle);
            DataGridAssist.SetCheckBoxEditingStyle(grid, editingStyle);
            DataGridAssist.SetUseUnifiedCellControls(grid, true);

            var generatedColumn = new DataGridCheckBoxColumn();
            grid.Columns.Add(generatedColumn);
            Ensure(ReferenceEquals(generatedColumn.ElementStyle, elementStyle),
                "DataGridCheckBoxColumn 仍在使用 WPF 系统默认显示样式。");
            Ensure(ReferenceEquals(generatedColumn.EditingElementStyle, editingStyle),
                "DataGridCheckBoxColumn 仍在使用 WPF 系统默认编辑样式。");

            var explicitStyle = new Style(typeof(CheckBox));
            var explicitColumn = new DataGridCheckBoxColumn { ElementStyle = explicitStyle };
            grid.Columns.Add(explicitColumn);
            Ensure(ReferenceEquals(explicitColumn.ElementStyle, explicitStyle),
                "统一样式覆盖了页面作者显式指定的列样式。");
        });
    }

    [Test]
    public static void ProgressRingDefaultsToZeroAndInactive()
    {
        RunSta(() =>
        {
            var ring = new ProgressRing();
            Ensure(Math.Abs(ring.Value) < double.Epsilon, "ProgressRing 默认 Value 不是 0。");
            Ensure(!ring.IsActive, "ProgressRing 默认状态仍会启动进度动画。");
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Ensure(thread.Join(TimeSpan.FromSeconds(10)), "WPF 控件测试超时。");
        if (failure is not null)
            throw new InvalidOperationException(failure.Message, failure);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
