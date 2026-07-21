using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace QemuWG.界面;

internal static class 按钮交互动画
{
    private static readonly ConditionalWeakTable<ButtonBase, object> 已启用按钮 = new();
    private static readonly UISettings 系统界面设置 = new();

    public static void 启用(DependencyObject root)
    {
        if (!系统界面设置.AnimationsEnabled) return;
        EnableDescendants(root);
    }

    private static void EnableDescendants(DependencyObject element)
    {
        if (element is ButtonBase button) EnableButton(button);
        var childCount = VisualTreeHelper.GetChildrenCount(element);
        for (var index = 0; index < childCount; index++)
            EnableDescendants(VisualTreeHelper.GetChild(element, index));
    }

    private static void EnableButton(ButtonBase button)
    {
        if (已启用按钮.TryGetValue(button, out _)) return;
        已启用按钮.Add(button, new object());
        button.PointerEntered += (_, _) => Animate(button, 1.01f, 170);
        button.PointerPressed += (_, _) => Animate(button, 0.985f, 90);
        button.PointerReleased += (_, _) => Animate(button, 1.01f, 160);
        button.PointerExited += (_, _) => Animate(button, 1, 220);
        button.PointerCanceled += (_, _) => Animate(button, 1, 220);
        button.PointerCaptureLost += (_, _) => Animate(button, 1, 220);
    }

    private static void Animate(ButtonBase element, float scale, int durationMilliseconds)
    {
        if (!element.IsEnabled) return;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        var compositor = visual.Compositor;
        var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(durationMilliseconds);
        animation.InsertKeyFrame(
            1,
            new Vector3(scale, scale, 1),
            compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1), new Vector2(0.3f, 1)));
        visual.StartAnimation("Scale", animation);
    }
}
