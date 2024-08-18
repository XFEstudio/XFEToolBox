using System.Windows.Media;
using System.Windows;

namespace XFEToolBox.Utilities;

public class ControlHelper
{
    public static T? FindControlByTag<T>(DependencyObject parent, object tag) where T : FrameworkElement
    {
        if (parent is null)
            return null;
        int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childrenCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            var childElement = child as T;
            if (childElement is not null && Equals(childElement.Tag, tag))
                return childElement;
            var result = FindControlByTag<T>(child, tag);
            if (result is not null)
                return result;
        }
        return null;
    }
}
