using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace QemuWG.界面;

internal static class 对话框布局
{
    private const double 对话框宽度比例 = 0.86;
    private const double 内容高度比例 = 0.64;
    private const double 对话框最大高度比例 = 0.9;

    public static void 启用自适应尺寸(ContentDialog dialog)
    {
        XamlRoot? root = null;
        var updating = false;

        void Update()
        {
            if (updating) return;
            root = dialog.XamlRoot;
            if (root is null || dialog.Content is not FrameworkElement content) return;

            var dialogWidth = root.Size.Width * 对话框宽度比例;
            var contentHeight = root.Size.Height * 内容高度比例;
            updating = true;
            try
            {
                dialog.Resources["ContentDialogMaxWidth"] = dialogWidth;
                dialog.Resources["ContentDialogMaxHeight"] = root.Size.Height * 对话框最大高度比例;
                content.Width = double.NaN;
                content.Height = contentHeight;
                content.HorizontalAlignment = HorizontalAlignment.Stretch;
                content.VerticalAlignment = VerticalAlignment.Stretch;
            }
            finally
            {
                updating = false;
            }
        }

        void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Update();

        dialog.Loading += (_, _) => Update();
        dialog.Opened += (_, _) =>
        {
            Update();
            if (root is not null) root.Changed += RootChanged;
        };
        dialog.Closed += (_, _) =>
        {
            if (root is not null) root.Changed -= RootChanged;
        };
    }

    public static void 应用按钮圆角(ContentDialog dialog)
    {
        dialog.CornerRadius = new CornerRadius(9);
        dialog.PrimaryButtonStyle = 创建按钮样式(dialog.DefaultButton == ContentDialogButton.Primary);
        dialog.SecondaryButtonStyle = 创建按钮样式(dialog.DefaultButton == ContentDialogButton.Secondary);
        dialog.CloseButtonStyle = 创建按钮样式(dialog.DefaultButton == ContentDialogButton.Close);
    }

    private static Style 创建按钮样式(bool emphasized)
    {
        var style = new Style { TargetType = typeof(Button) };
        var baseStyleKey = emphasized ? "AccentButtonStyle" : "DefaultButtonStyle";
        if (Application.Current.Resources.TryGetValue(baseStyleKey, out var value) && value is Style baseStyle)
            style.BasedOn = baseStyle;
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(6)));
        return style;
    }
}
