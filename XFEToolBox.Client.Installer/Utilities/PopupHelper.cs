using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XFEToolBox.Client.Installer.Model;
using XFEToolBox.Client.Installer.Views.Pages.Popups;
using XFEToolBox.Client.Installer.Views.Windows;

namespace XFEToolBox.Client.Installer.Utilities
{
    public static class PopupHelper
    {
        private static NormalDialogPopupPage CreateNormalDialogPage(object content)
        {
            var dialogPage = new NormalDialogPopupPage();
            dialogPage.ViewModel.Content = content;
            return dialogPage;
        }

        private static ScrollViewer CreateTextContent(string text, Color textColor) => new()
        {
            Content = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(textColor),
                Margin = new Thickness(20, 20, 20, 0),
                TextWrapping = TextWrapping.WrapWithOverflow
            },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        public static MessageBoxResult? ShowConfirmDialog(object content, bool showCancelButton = false, string confirmText = "确定", string cancelText = "取消")
        {
            var dialog = CreateNormalDialogPage(content);
            dialog.ViewModel.ConfirmText = confirmText;
            dialog.ViewModel.CancelText = cancelText;
            dialog.ViewModel.ConfirmGridLength = new GridLength(1, GridUnitType.Star);
            if (showCancelButton)
                dialog.ViewModel.CancelGridLength = new GridLength(1, GridUnitType.Star);
            return ShowDialog(dialog);
        }

        public static MessageBoxResult? ShowConfirmDialog(string text, Color textColor, bool showCancelButton = false, string confirmText = "确定", string cancelText = "取消") => ShowConfirmDialog(CreateTextContent(text, textColor), showCancelButton, confirmText, cancelText);

        public static MessageBoxResult? ShowConfirmDialog(string text, bool showCancelButton = false, string confirmText = "确定", string cancelText = "取消") => ShowConfirmDialog(text, Color.FromRgb(70, 70, 92), showCancelButton, confirmText, cancelText);

        public static MessageBoxResult? ShowYesOrNoDialog(object content, bool showCancelButton = false, string yesText = "是", string noText = "否")
        {
            var dialog = CreateNormalDialogPage(content);
            dialog.ViewModel.YesText = yesText;
            dialog.ViewModel.NoText = noText;
            dialog.ViewModel.YesGridLength = new GridLength(1, GridUnitType.Star);
            dialog.ViewModel.NoGridLength = new GridLength(1, GridUnitType.Star);
            if (showCancelButton)
                dialog.ViewModel.CancelGridLength = new GridLength(1, GridUnitType.Star);
            return ShowDialog(dialog);
        }

        public static MessageBoxResult? ShowYesOrNoDialog(string text, Color textColor, bool showCancelButton = false, string yesText = "是", string noText = "否") => ShowYesOrNoDialog(CreateTextContent(text, textColor), showCancelButton, yesText, noText);

        public static MessageBoxResult? ShowYesOrNoDialog(string text, bool showCancelButton = false, string yesText = "是", string noText = "否") => ShowYesOrNoDialog(text, Color.FromRgb(70, 70, 92), showCancelButton, yesText, noText);

        public static MessageBoxResult? ShowDialog(object content, double width = 320, double height = 230)
        {
            var popupWindow = new PopupWindow
            {
                Width = width,
                Height = height,
                Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
                        ?? MainWindow.Current
            };
            popupWindow.ViewModel.Content = content;
            if (content is IPopupPage popupPage)
                popupPage.PopupWindow = popupWindow;
            var mainWindow = popupWindow.Owner as MainWindow;
            mainWindow?.SetModalShade(true);
            try
            {
                popupWindow.ShowDialog();
                return popupWindow.Result;
            }
            finally
            {
                mainWindow?.SetModalShade(false);
            }
        }
    }
}
