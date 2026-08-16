# 通用弹窗

`PopupWindow` 提供与主窗口一致的圆角、顶部拖拽横条、关闭按钮和开合动画。业务页面不需要自行创建 Window，只需交给 `PopupHelper` 显示。

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

需要主动关闭并返回结果的页面可实现 `IPopupPage`：

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

不需要标题栏文字时保持 `Title` 和 `Subtitle` 为空即可，内容区域会自动占用这部分空间。旧的 `ShowDialog(content, width, height)` 调用方式继续可用。
