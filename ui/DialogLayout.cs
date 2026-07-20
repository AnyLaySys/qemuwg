using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace QemuWG.ui;

internal static class DialogLayout
{
    private const double WidthRatio = 0.9;
    private const double HeightRatio = 0.8;

    public static void EnableAdaptiveSizing(ContentDialog dialog)
    {
        XamlRoot? root = null;
        var updating = false;

        void Update()
        {
            if (updating) return;
            root = dialog.XamlRoot;
            if (root is null || dialog.Content is not FrameworkElement content) return;

            var width = root.Size.Width * WidthRatio;
            var height = root.Size.Height * HeightRatio;
            updating = true;
            try
            {
                dialog.Resources["ContentDialogMaxWidth"] = width;
                if (Math.Abs(content.Width - width) > 0.5) content.Width = width;
                if (Math.Abs(content.Height - height) > 0.5) content.Height = height;
            }
            finally
            {
                updating = false;
            }
        }

        void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Update();

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
}
