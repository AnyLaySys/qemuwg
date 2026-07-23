using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace QemuWG.界面;

internal sealed class 滚动激活保护
{
    private const double 偏移容差 = 0.5;
    private const int 解冻所需稳定帧 = 6;
    private static readonly HashSet<string> 页面滚动器名称 =
    [
        "ContentScrollViewer",
        "DetailScrollViewer",
        "DiskFormScrollViewer",
        "EditorScrollViewer"
    ];

    private readonly DispatcherQueue dispatcherQueue;
    private readonly List<滚动快照> snapshots = [];
    private bool frozen;
    private bool applying;
    private bool restoreQueued;
    private bool renderingSubscribed;
    private int stableFrames;
    private bool blockedRequestLogged;
    private bool driftLogged;

    public 滚动激活保护(DispatcherQueue dispatcherQueue)
    {
        this.dispatcherQueue = dispatcherQueue;
    }

    public void 更新窗口状态(WindowActivationState state, params DependencyObject?[] roots)
    {
        if (state == WindowActivationState.Deactivated)
        {
            冻结(roots);
            return;
        }

        if (!frozen) return;
        恢复偏移();
        stableFrames = 0;
        if (!renderingSubscribed)
        {
            CompositionTarget.Rendering += CompositionTarget_Rendering;
            renderingSubscribed = true;
        }

        dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (frozen) 恢复偏移();
        });
    }

    public void 停止()
    {
        if (frozen) 恢复偏移();
        解冻();
    }

    private void 冻结(IEnumerable<DependencyObject?> roots)
    {
        if (frozen)
        {
            恢复偏移();
            解冻();
        }

        snapshots.Clear();
        var found = new HashSet<ScrollViewer>();
        foreach (var root in roots)
        {
            if (root is not null) 查找页面滚动器(root, found);
        }

        foreach (var scrollViewer in found)
        {
            if (!scrollViewer.IsLoaded) continue;
            var contentRoot = scrollViewer.Content as UIElement;
            contentRoot?.CancelDirectManipulations();
            var snapshot = new 滚动快照(
                scrollViewer,
                contentRoot,
                scrollViewer.HorizontalOffset,
                scrollViewer.VerticalOffset,
                scrollViewer.ZoomFactor,
                scrollViewer.BringIntoViewOnFocusChange);
            snapshots.Add(snapshot);
            scrollViewer.BringIntoViewOnFocusChange = false;
            scrollViewer.ViewChanged += ScrollViewer_ViewChanged;
            if (contentRoot is not null)
                contentRoot.BringIntoViewRequested += ContentRoot_BringIntoViewRequested;
        }

        frozen = true;
        stableFrames = 0;
        blockedRequestLogged = false;
        driftLogged = false;
    }

    private static void 查找页面滚动器(DependencyObject root, HashSet<ScrollViewer> result)
    {
        if (root is ScrollViewer scrollViewer && 页面滚动器名称.Contains(scrollViewer.Name))
        {
            result.Add(scrollViewer);
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
            查找页面滚动器(VisualTreeHelper.GetChild(root, index), result);
    }

    private void ContentRoot_BringIntoViewRequested(UIElement sender, BringIntoViewRequestedEventArgs args)
    {
        if (!frozen) return;
        args.Handled = true;
        stableFrames = 0;
        if (blockedRequestLogged) return;
        blockedRequestLogged = true;
        var target = args.TargetElement as FrameworkElement;
        应用日志.写($"Blocked activation BringIntoView request: target={target?.Name ?? target?.GetType().Name ?? "unknown"}.");
    }

    private void ScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (!frozen || applying || restoreQueued) return;
        stableFrames = 0;
        restoreQueued = true;
        dispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, () =>
        {
            restoreQueued = false;
            if (frozen) 恢复偏移();
        });
    }

    private void CompositionTarget_Rendering(object? sender, object args)
    {
        if (!frozen)
        {
            取消渲染监听();
            return;
        }

        var stable = 恢复偏移();
        stableFrames = stable ? stableFrames + 1 : 0;
        if (stableFrames >= 解冻所需稳定帧) 解冻();
    }

    private bool 恢复偏移()
    {
        if (applying) return false;
        applying = true;
        var stable = true;
        try
        {
            foreach (var snapshot in snapshots)
            {
                var scrollViewer = snapshot.ScrollViewer;
                if (!scrollViewer.IsLoaded) continue;
                if ((snapshot.HorizontalOffset > 0 && scrollViewer.ScrollableWidth <= 0)
                    || (snapshot.VerticalOffset > 0 && scrollViewer.ScrollableHeight <= 0))
                {
                    stable = false;
                    continue;
                }

                var horizontalOffset = Math.Min(snapshot.HorizontalOffset, scrollViewer.ScrollableWidth);
                var verticalOffset = Math.Min(snapshot.VerticalOffset, scrollViewer.ScrollableHeight);
                if (Math.Abs(scrollViewer.HorizontalOffset - horizontalOffset) <= 偏移容差
                    && Math.Abs(scrollViewer.VerticalOffset - verticalOffset) <= 偏移容差
                    && Math.Abs(scrollViewer.ZoomFactor - snapshot.ZoomFactor) <= 偏移容差)
                {
                    continue;
                }

                stable = false;
                if (!driftLogged)
                {
                    driftLogged = true;
                    应用日志.写(
                        $"Activation scroll drift blocked: viewer={scrollViewer.Name}, " +
                        $"from={scrollViewer.VerticalOffset:F1}, to={verticalOffset:F1}.");
                }
                scrollViewer.ChangeView(horizontalOffset, verticalOffset, snapshot.ZoomFactor, true);
            }
        }
        finally
        {
            applying = false;
        }

        return stable;
    }

    private void 解冻()
    {
        foreach (var snapshot in snapshots)
        {
            snapshot.ScrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
            snapshot.ScrollViewer.BringIntoViewOnFocusChange = snapshot.BringIntoViewOnFocusChange;
            if (snapshot.ContentRoot is not null)
                snapshot.ContentRoot.BringIntoViewRequested -= ContentRoot_BringIntoViewRequested;
        }

        snapshots.Clear();
        frozen = false;
        applying = false;
        restoreQueued = false;
        stableFrames = 0;
        取消渲染监听();
    }

    private void 取消渲染监听()
    {
        if (!renderingSubscribed) return;
        CompositionTarget.Rendering -= CompositionTarget_Rendering;
        renderingSubscribed = false;
    }

    private sealed record 滚动快照(
        ScrollViewer ScrollViewer,
        UIElement? ContentRoot,
        double HorizontalOffset,
        double VerticalOffset,
        float ZoomFactor,
        bool BringIntoViewOnFocusChange);
}
