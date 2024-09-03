using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using XFEToolBox.Views.Pages.Popups;
using XFEToolBox.Views.Windows;

namespace XFEToolBox.Utilities;

public static class PopupHelper
{
    public static NormalDialogPopupPage CreateNormalDialogPage(object content, PopupWindow popupWindow)
    {
        var dialogPage = new NormalDialogPopupPage(popupWindow);
        dialogPage.ViewModel.Content = content;
        return dialogPage;
    }

    public static MessageBoxResult? ShowNormalDialog(string text, Color textColor)
    {
        var popupWindow = new PopupWindow();
        var dialogPage = CreateNormalDialogPage(new ScrollViewer()
        {
            Content = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(textColor),
                Margin = new Thickness(20, 20, 20, 0)
            },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Resources = new ResourceDictionary
            {
                {
                    "scroll",
                    new Style
                    {
                        TargetType = typeof(ScrollBar),
                        BasedOn = (Style)Application.Current.FindResource("ConsoleScrollBar")
                    }
                }
            }
        }, popupWindow);
        popupWindow.ViewModel.Content = dialogPage;
        popupWindow.ShowDialog();
        return popupWindow.Result;
    }

    public static MessageBoxResult? ShowNormalDialog(string text) => ShowNormalDialog(text, Colors.Black);
}
