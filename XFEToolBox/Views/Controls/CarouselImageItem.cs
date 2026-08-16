using System.ComponentModel;
using System.Windows.Media;

namespace XFEToolBox.Client.Views.Controls;

public class CarouselImageItem : INotifyPropertyChanged
{
    private ImageSource? image;
    public ImageSource? Image { get => image; set { image = value; OnPropertyChanged(nameof(Image)); } }

    private string title = string.Empty;
    public string Title { get => title; set { title = value; OnPropertyChanged(nameof(Title)); } }

    private bool isSelected = false;
    public bool IsSelected { get => isSelected; set { isSelected = value; OnPropertyChanged(nameof(IsSelected)); } }

    private Action? action;
    public Action? Action { get => action; set { action = value; OnPropertyChanged(nameof(Action)); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
