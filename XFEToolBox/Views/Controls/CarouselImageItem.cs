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

    public Action? Action { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
