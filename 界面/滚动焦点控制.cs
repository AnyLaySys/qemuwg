using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace QemuWG.界面;

internal static class 滚动焦点控制
{
    public static void 禁用自动滚动(DependencyObject root)
    {
        if (root is ScrollViewer scrollViewer)
            scrollViewer.BringIntoViewOnFocusChange = false;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
            禁用自动滚动(VisualTreeHelper.GetChild(root, index));
    }
}
