using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Views.Windows;

namespace XFEToolBox.Client.Views.Pages.Popups;

public partial class ToolConfigurationPopupPage : Page, IPopupPage
{
    public ToolConfigurationPopupPage(string toolName, bool runAsAdministrator, ImageSource shieldIcon)
    {
        InitializeComponent();
        ToolNameText.Text = toolName;
        RunAsAdministratorCheckBox.IsChecked = runAsAdministrator;
        ShieldImage.Source = shieldIcon;
    }

    public PopupWindow? PopupWindow { get; set; }

    public bool RunAsAdministrator { get; private set; }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        RunAsAdministrator = RunAsAdministratorCheckBox.IsChecked == true;
        if (PopupWindow is not null)
            await PopupWindow.CloseWithResultAsync(MessageBoxResult.OK);
    }

    private async void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (PopupWindow is not null)
            await PopupWindow.CloseWithResultAsync(MessageBoxResult.Cancel);
    }
}
