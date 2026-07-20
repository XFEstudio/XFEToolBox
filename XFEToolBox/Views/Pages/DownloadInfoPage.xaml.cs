using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using XFEToolBox.Client.ViewModel.Pages;
using DownloadInfoPageViewModel = XFEToolBox.Client.ViewModel.Pages.DownloadInfoPageViewModel;

namespace XFEToolBox.Client.Views.Pages;

/// <summary>
/// DownloadInfoPage.xaml 的交互逻辑
/// </summary>
public partial class DownloadInfoPage : Page
{
    public DownloadInfoPageViewModel ViewModel { get; set; }

    private bool downloadMode;

    public bool DownloadMode
    {
        get { return downloadMode; }
        set { downloadMode = value; ViewModel.DownloadButtonName = value ? "下载" : "浏览器中打开"; }
    }

    public DownloadInfoPage()
    {
        DataContext = ViewModel = new(this);
        InitializeComponent();
        UpdateArc(180);
    }

    public void UpdateArc(double angle)
    {
        double radius = 100;  // 圆的半径
        double radians = Math.PI * angle / 180.0;

        // 计算圆弧的终点坐标
        double x = 100 + radius * Math.Cos(radians);
        double y = 100 - radius * Math.Sin(radians);

        // 更新 ArcSegment 的终点
        arcSegment.Point = new Point(x, y);

        // 根据角度判断是否需要大圆弧
        arcSegment.IsLargeArc = angle > 180.0;
    }

    public void UpperTheGrid()
    {
        bottomGrid.BeginAnimation(HeightProperty, null);
        var doubleAnimation = new DoubleAnimation
        {
            To = 400,
            Duration = TimeSpan.FromMilliseconds(500),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        bottomGrid.BeginAnimation(HeightProperty, doubleAnimation);
    }

    public void LowerTheGrid()
    {
        bottomGrid.BeginAnimation(HeightProperty, null);
        var doubleAnimation = new DoubleAnimation
        {
            To = 100,
            Duration = TimeSpan.FromMilliseconds(500),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        bottomGrid.BeginAnimation(HeightProperty, doubleAnimation);
    }
}
