using XFEToolBox.ViewModel;
using System.Windows;

namespace XFEToolBox.Views.Windows
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static MainWindow? Current { get; private set; }
        public MainWindowViewModel ViewModel { get; private set; }
        public MainWindow()
        {
            InitializeComponent();
            ViewModel = new MainWindowViewModel(this);
            DataContext = ViewModel;
            Current = this;
        }
    }
}