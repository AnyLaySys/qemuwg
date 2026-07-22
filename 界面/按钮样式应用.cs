using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace QemuWG.界面;

public static class 按钮样式应用
{
    public static void 设为正方形(Button button)
    {
        if (Application.Current.Resources.TryGetValue("正方形按钮样式", out var resource)
            && resource is Style style)
            button.Style = style;
    }
}
