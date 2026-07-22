using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace QemuWG.界面;

internal static class 页面过渡动画
{
    private static readonly ConditionalWeakTable<TabView, 标签页状态> 已启用标签页 = new();
    private static readonly ConditionalWeakTable<FrameworkElement, 可见性动画状态> 可见性动画 = new();
    private static readonly UISettings 系统界面设置 = new();

    private sealed class 标签页状态
    {
        public FrameworkElement? 上次内容 { get; set; }
    }

    private sealed class 可见性动画状态
    {
        public long 版本;
        public Vector3? 待恢复偏移;
    }

    public static Task 渐进显示(FrameworkElement? element, double verticalOffset = 12)
    {
        if (element is null) return Task.CompletedTask;

        var animationState = 可见性动画.GetOrCreateValue(element);
        Interlocked.Increment(ref animationState.版本);
        element.Visibility = Visibility.Visible;
        element.IsHitTestVisible = true;
        element.Opacity = 1;
        if (!系统界面设置.AnimationsEnabled)
        {
            return Task.CompletedTask;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        停止显现动画(visual);
        恢复隐藏动画偏移(visual, animationState);
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        var compositor = visual.Compositor;
        var easing = 创建缓动(compositor);
        var targetOffset = visual.Offset;

        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.Duration = TimeSpan.FromMilliseconds(210);
        opacity.InsertKeyFrame(0, 0);
        opacity.InsertKeyFrame(1, 1, easing);
        visual.StartAnimation("Opacity", opacity);

        var offset = compositor.CreateVector3KeyFrameAnimation();
        offset.Duration = TimeSpan.FromMilliseconds(260);
        offset.InsertKeyFrame(0, targetOffset + new Vector3(0, (float)verticalOffset, 0));
        offset.InsertKeyFrame(1, targetOffset, easing);
        visual.StartAnimation("Offset", offset);

        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.Duration = TimeSpan.FromMilliseconds(280);
        scale.InsertKeyFrame(0, new Vector3(0.985f, 0.985f, 1));
        scale.InsertKeyFrame(1, Vector3.One, easing);
        visual.StartAnimation("Scale", scale);
        return Task.CompletedTask;
    }

    public static void 启用标签页动画(TabView tabView)
    {
        if (已启用标签页.TryGetValue(tabView, out _)) return;
        var state = new 标签页状态();
        已启用标签页.Add(tabView, state);
        tabView.SelectionChanged += (_, _) =>
        {
            if (tabView.IsLoaded) AnimateSelectedTab(tabView, state);
        };
        tabView.Loaded += (_, _) => AnimateSelectedTab(tabView, state);
    }

    private static void AnimateSelectedTab(TabView tabView, 标签页状态 state)
    {
        if (tabView.SelectedItem is not TabViewItem { Content: FrameworkElement content }) return;
        if (ReferenceEquals(state.上次内容, content)) return;
        state.上次内容 = content;
        content.DispatcherQueue.TryEnqueue(() => _ = 渐进显示(content, 9));
    }

    public static void 渐进隐藏(FrameworkElement? element, double horizontalOffset = 6)
    {
        if (element is null || element.Visibility != Visibility.Visible) return;
        var animationState = 可见性动画.GetOrCreateValue(element);
        var version = Interlocked.Increment(ref animationState.版本);
        element.IsHitTestVisible = false;
        if (!系统界面设置.AnimationsEnabled)
        {
            element.Visibility = Visibility.Collapsed;
            element.IsHitTestVisible = true;
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        停止显现动画(visual);
        var compositor = visual.Compositor;
        var targetOffset = visual.Offset;
        animationState.待恢复偏移 = targetOffset;
        var easing = 创建缓动(compositor);
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.Duration = TimeSpan.FromMilliseconds(120);
        opacity.InsertKeyFrame(0, 1);
        opacity.InsertKeyFrame(1, 0, easing);
        visual.StartAnimation("Opacity", opacity);

        var offset = compositor.CreateVector3KeyFrameAnimation();
        offset.Duration = TimeSpan.FromMilliseconds(150);
        offset.InsertKeyFrame(0, targetOffset);
        offset.InsertKeyFrame(1, targetOffset + new Vector3((float)horizontalOffset, 0, 0), easing);
        visual.StartAnimation("Offset", offset);
        batch.Completed += (_, _) =>
        {
            if (Volatile.Read(ref animationState.版本) != version) return;
            element.Visibility = Visibility.Collapsed;
            element.IsHitTestVisible = true;
            停止显现动画(visual);
            恢复隐藏动画偏移(visual, animationState);
        };
        batch.End();
    }

    public static void 布局稳定(params FrameworkElement?[] elements)
    {
        if (!系统界面设置.AnimationsEnabled) return;
        foreach (var element in elements)
        {
            if (element is null || element.Visibility != Visibility.Visible) continue;
            var visual = ElementCompositionPreview.GetElementVisual(element);
            停止显现动画(visual);
            visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
            var compositor = visual.Compositor;
            var easing = 创建缓动(compositor);
            var targetOffset = visual.Offset;

            var offset = compositor.CreateVector3KeyFrameAnimation();
            offset.Duration = TimeSpan.FromMilliseconds(180);
            offset.InsertKeyFrame(0, targetOffset + new Vector3(0, 3, 0));
            offset.InsertKeyFrame(1, targetOffset, easing);
            visual.StartAnimation("Offset", offset);

            var scale = compositor.CreateVector3KeyFrameAnimation();
            scale.Duration = TimeSpan.FromMilliseconds(220);
            scale.InsertKeyFrame(0, new Vector3(0.996f, 0.996f, 1));
            scale.InsertKeyFrame(1, Vector3.One, easing);
            visual.StartAnimation("Scale", scale);
        }
    }

    private static CubicBezierEasingFunction 创建缓动(Compositor compositor) =>
        compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1), new Vector2(0.3f, 1));

    private static void 停止显现动画(Visual visual)
    {
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Offset");
        visual.StopAnimation("Scale");
        visual.Opacity = 1;
        visual.Scale = Vector3.One;
    }

    private static void 恢复隐藏动画偏移(Visual visual, 可见性动画状态 animationState)
    {
        if (animationState.待恢复偏移 is not { } offset) return;
        visual.Offset = offset;
        animationState.待恢复偏移 = null;
    }
}
