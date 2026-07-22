using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace QemuWG.界面;

internal static class 对话框布局
{
    private const double 对话框宽度比例 = 0.86;
    private const double 内容高度比例 = 0.64;
    private const double 对话框最大高度比例 = 0.9;

    public static void 启用自适应尺寸(ContentDialog dialog)
    {
        var updating = false;
        var lastWidth = double.NaN;
        var lastHeight = double.NaN;

        void Update()
        {
            if (updating) return;
            var root = dialog.XamlRoot;
            if (root is null || dialog.Content is not FrameworkElement content) return;

            var dialogWidth = root.Size.Width * 对话框宽度比例;
            var contentHeight = root.Size.Height * 内容高度比例;
            var dialogMaxHeight = root.Size.Height * 对话框最大高度比例;
            if (Math.Abs(lastWidth - dialogWidth) < 0.5 && Math.Abs(lastHeight - contentHeight) < 0.5) return;

            updating = true;
            try
            {
                if (!dialog.Resources.TryGetValue("ContentDialogMaxWidth", out var widthValue)
                    || widthValue is not double currentWidth
                    || Math.Abs(currentWidth - dialogWidth) >= 0.5)
                    dialog.Resources["ContentDialogMaxWidth"] = dialogWidth;
                if (!dialog.Resources.TryGetValue("ContentDialogMaxHeight", out var heightValue)
                    || heightValue is not double currentHeight
                    || Math.Abs(currentHeight - dialogMaxHeight) >= 0.5)
                    dialog.Resources["ContentDialogMaxHeight"] = dialogMaxHeight;
                if (!double.IsNaN(content.Width)) content.Width = double.NaN;
                if (double.IsNaN(content.Height) || Math.Abs(content.Height - contentHeight) >= 0.5) content.Height = contentHeight;
                content.HorizontalAlignment = HorizontalAlignment.Stretch;
                content.VerticalAlignment = VerticalAlignment.Stretch;
                lastWidth = dialogWidth;
                lastHeight = contentHeight;
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
            if (dialog.XamlRoot is { } root)
            {
                root.Changed -= RootChanged;
                root.Changed += RootChanged;
            }
        };
        dialog.Closed += (_, _) =>
        {
            if (dialog.XamlRoot is { } root) root.Changed -= RootChanged;
        };
        dialog.Closing += (_, _) =>
        {
            var smokeLayer = 查找模板元素(dialog, "SmokeLayerBackground");
            if (smokeLayer is not null) smokeLayer.Opacity = 0;
        };
    }

    private static FrameworkElement? 查找模板元素(DependencyObject root, string name)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement { Name: var childName } element && childName == name) return element;
            var match = 查找模板元素(child, name);
            if (match is not null) return match;
        }
        return null;
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
