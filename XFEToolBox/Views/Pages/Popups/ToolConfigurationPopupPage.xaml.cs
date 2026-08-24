using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Views.Windows;

namespace XFEToolBox.Client.Views.Pages.Popups;

public partial class ToolConfigurationPopupPage : Page, IPopupPage
{
    private readonly bool _requiresAdministrator;
    private readonly bool _initialUserPreference;

    public ToolConfigurationPopupPage(
        string toolName,
        bool userRunAsAdministrator,
        bool requiresAdministrator,
        ImageSource shieldIcon)
    {
        InitializeComponent();
        _requiresAdministrator = requiresAdministrator;
        _initialUserPreference = userRunAsAdministrator;
        ToolNameText.Text = toolName;
        RunAsAdministratorCheckBox.IsChecked = requiresAdministrator || userRunAsAdministrator;
        RunAsAdministratorCheckBox.IsEnabled = !requiresAdministrator;
        ManifestRequirementText.Visibility = requiresAdministrator ? Visibility.Visible : Visibility.Collapsed;
        ShieldImage.Source = shieldIcon;
    }

    public PopupWindow? PopupWindow { get; set; }

    public bool UserRunAsAdministrator { get; private set; }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        UserRunAsAdministrator = _requiresAdministrator
            ? _initialUserPreference
            : RunAsAdministratorCheckBox.IsChecked == true;
        if (PopupWindow is not null)
            await PopupWindow.CloseWithResultAsync(MessageBoxResult.OK);
    }

    private async void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (PopupWindow is not null)
            await PopupWindow.CloseWithResultAsync(MessageBoxResult.Cancel);
    }
}
