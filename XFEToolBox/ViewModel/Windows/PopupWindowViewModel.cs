using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using XFEToolBox.Client.Views.Windows;

namespace XFEToolBox.Client.ViewModel.Windows;

public partial class PopupWindowViewModel(PopupWindow viewPage) : ObservableObject
{
    [ObservableProperty]
    object? content;
    [ObservableProperty]
    string popupTitle = string.Empty;
    [ObservableProperty]
    string popupSubtitle = string.Empty;
    [ObservableProperty]
    Thickness contentMargin = new(0, 0, 0, 15);
    [ObservableProperty]
    Brush popupBackground = new SolidColorBrush(Color.FromRgb(152, 152, 231));
    [ObservableProperty]
    Brush popupContentBackground = new SolidColorBrush(Colors.White);
    [ObservableProperty]
    Visibility closeButtonVisibility = Visibility.Visible;
    [ObservableProperty]
    Visibility moveButtonVisibility = Visibility.Visible;

    public PopupWindow ViewPage { get; set; } = viewPage;

    public GridLength HeaderGridLength => string.IsNullOrWhiteSpace(PopupTitle) && string.IsNullOrWhiteSpace(PopupSubtitle)
        ? new GridLength(0)
        : new GridLength(42);

    partial void OnPopupTitleChanged(string value) => OnPropertyChanged(nameof(HeaderGridLength));

    partial void OnPopupSubtitleChanged(string value) => OnPropertyChanged(nameof(HeaderGridLength));
}
