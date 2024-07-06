using System.Windows;
using System.Windows.Controls;

namespace XFEToolBox.Views.Controls;

public class NavigationButton : RadioButton
{
    static NavigationButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(NavigationButton), new PropertyMetadata(typeof(NavigationButton)));
    }
}
