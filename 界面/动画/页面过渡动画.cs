using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace QemuWG.界面;

internal static class 页面过渡动画
{
    private static readonly ConditionalWeakTable<TabView, object> 已启用标签页 = new();
    private static readonly UISettings 系统界面设置 = new();

    public static async Task 渐进显示(FrameworkElement? element, double verticalOffset = 12)
    {
        if (element is null) return;
        element.Visibility = Visibility.Visible;
        if (!系统界面设置.AnimationsEnabled)
        {
            element.Opacity = 1;
            element.RenderTransform = null;
            return;
        }

        var transform = new CompositeTransform
        {
            TranslateY = verticalOffset,
            ScaleX = 0.985,
            ScaleY = 0.985
        };
        element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        element.RenderTransform = transform;
        element.Opacity = 0;

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var storyboard = new Storyboard();
        storyboard.Children.Add(CreateAnimation(element, "Opacity", 1, 210, easing, false));
        storyboard.Children.Add(CreateAnimation(transform, "TranslateY", 0, 280, easing, true));
        storyboard.Children.Add(CreateAnimation(transform, "ScaleX", 1, 300, easing, true));
        storyboard.Children.Add(CreateAnimation(transform, "ScaleY", 1, 300, easing, true));

        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        storyboard.Completed += (_, _) => completed.TrySetResult(true);
        storyboard.Begin();
        await completed.Task;
    }

    public static void 启用标签页动画(TabView tabView)
    {
        if (已启用标签页.TryGetValue(tabView, out _)) return;
        已启用标签页.Add(tabView, new object());
        tabView.SelectionChanged += (_, _) => AnimateSelectedTab(tabView);
        tabView.Loaded += (_, _) => AnimateSelectedTab(tabView);
    }

    private static void AnimateSelectedTab(TabView tabView)
    {
        if (tabView.SelectedItem is not TabViewItem { Content: FrameworkElement content }) return;
        content.DispatcherQueue.TryEnqueue(() => _ = 渐进显示(content, 9));
    }

    private static DoubleAnimation CreateAnimation(
        DependencyObject target,
        string property,
        double value,
        int durationMilliseconds,
        EasingFunctionBase easing,
        bool allowDependentAnimation)
    {
        var animation = new DoubleAnimation
        {
            To = value,
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMilliseconds)),
            EasingFunction = easing,
            EnableDependentAnimation = allowDependentAnimation
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }
}
