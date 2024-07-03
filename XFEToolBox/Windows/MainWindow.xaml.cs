using System.Windows;

namespace FileSystemWatcher.Windows
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static MainWindow? Current { get; private set; }
        public MainWindow()
        {
            InitializeComponent();
            Current = this;
        }
    }
}