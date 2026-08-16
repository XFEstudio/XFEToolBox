# 通用弹窗

`PopupWindow` 提供与主窗口一致的圆角、顶部拖拽区、关闭按钮、背景变暗和开合动画。业务页面不需要自行创建 `Window`，只需通过 `PopupHelper` 显示页面或控件。

## 基本用法

```csharp
var result = PopupHelper.ShowDialog(new MyPopupPage(), new PopupWindowOptions
{
    Title = "弹窗标题",
    Subtitle = "可选的副标题",
    Width = 420,
    Height = 480,
    Owner = MainWindow.Current,
    ContentMargin = new Thickness(0)
});
```

`Owner` 为空时会自动选择当前活动的非弹窗窗口。`ShowDialog` 是模态调用，关闭后返回可空的 `MessageBoxResult`。

## 返回结果

需要主动关闭并返回结果的页面可以实现 `IPopupPage`：

```csharp
public partial class MyPopupPage : Page, IPopupPage
{
    public PopupWindow? PopupWindow { get; set; }

    private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (PopupWindow is not null)
            await PopupWindow.CloseWithResultAsync(MessageBoxResult.OK);
    }
}
```

`PopupHelper` 会在显示前自动设置 `IPopupPage.PopupWindow`。使用窗口关闭按钮时，结果通常为 `null`；业务方应显式处理取消或无结果情况。

## 显示选项

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `Title` | 空 | 标题；标题与副标题都为空时内容区自动占用标题空间 |
| `Subtitle` | 空 | 副标题 |
| `Width` / `Height` | `320` / `230` | 弹窗尺寸 |
| `Owner` | 活动窗口 | 所属窗口 |
| `ContentMargin` | `0,0,0,15` | 内容区域边距 |
| `ShowCloseButton` | `true` | 是否显示关闭按钮 |
| `ShowDragBar` | `true` | 是否显示并启用顶部拖拽区 |
| `DimOwner` | `true` | 显示时是否降低 Owner 不透明度并在关闭后恢复 |

## 快捷对话框

普通确认框：

```csharp
var result = PopupHelper.ShowConfirmDialog(
    "确定要保存修改吗？",
    showCancelButton: true,
    confirmText: "保存",
    cancelText: "取消");
```

是/否选择框：

```csharp
var result = PopupHelper.ShowYesOrNoDialog(
    "是否发布当前版本？",
    showCancelButton: true,
    yesText: "发布",
    noText: "暂不发布");
```

旧的 `ShowDialog(content, width, height)` 重载继续可用，适合不需要标题或其他交互选项的简单内容。
