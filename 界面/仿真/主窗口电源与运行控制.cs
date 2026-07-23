using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QemuWG.数据;
using QemuWG.服务;
using QemuWG.界面;

namespace QemuWG;

public sealed partial class 主窗
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? powerLongPressTimer;
    private bool powerLongPressTriggered;
    private bool suppressPowerClick;
    private bool powerOperationInProgress;
    private bool powerSecondaryButtonsVisible;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? powerSecondaryCollapseTimer;
    private bool paused;
    private 仿真配置? powerLongPressTarget;
    private DateTimeOffset powerPressStartedAt;

    private void 初始化电源按钮指针监听()
    {
        PowerButton.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(PowerButton_PointerPressed),
            true);
        PowerButton.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(PowerButton_PointerReleased),
            true);
        PowerButton.AddHandler(
            UIElement.PointerCanceledEvent,
            new PointerEventHandler(PowerButton_PointerCanceled),
            true);
        PowerButton.PointerCaptureLost += PowerButton_PointerCanceled;
    }

    private void PowerActionsHost_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        powerSecondaryCollapseTimer?.Stop();
        if (selectedVm?.IsRunning != true || powerSecondaryButtonsVisible) return;
        powerSecondaryButtonsVisible = true;
        _ = 页面过渡动画.渐进显示(PowerSecondaryButtons, 0);
    }

    private void PowerActionsHost_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!powerSecondaryButtonsVisible) return;
        powerSecondaryCollapseTimer ??= 创建电源次级收起计时器();
        powerSecondaryCollapseTimer.Stop();
        powerSecondaryCollapseTimer.Start();
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer 创建电源次级收起计时器()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(300);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (!powerSecondaryButtonsVisible) return;
            powerSecondaryButtonsVisible = false;
            页面过渡动画.渐进隐藏(PowerSecondaryButtons, 0);
        };
        return timer;
    }

    private void PowerButton_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (selectedVm?.IsRunning != true || !e.GetCurrentPoint(PowerButton).Properties.IsLeftButtonPressed) return;
        powerLongPressTarget = selectedVm;
        powerPressStartedAt = DateTimeOffset.UtcNow;
        powerLongPressTriggered = false;
        PowerButton.CapturePointer(e.Pointer);
        powerLongPressTimer ??= 创建电源长按计时器();
        powerLongPressTimer.Stop();
        powerLongPressTimer.Start();
    }

    private async void PowerButton_PointerReleased(object sender, PointerRoutedEventArgs e) => await 结束电源按压();

    private async void PowerButton_PointerCanceled(object sender, PointerRoutedEventArgs e) => await 结束电源按压();

    private Microsoft.UI.Dispatching.DispatcherQueueTimer 创建电源长按计时器()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(2700);
        timer.IsRepeating = false;
        timer.Tick += async (_, _) => await 强制停止长按目标();
        return timer;
    }

    private async Task 结束电源按压()
    {
        powerLongPressTimer?.Stop();
        var target = powerLongPressTarget;
        var elapsed = DateTimeOffset.UtcNow - powerPressStartedAt;
        if (!powerLongPressTriggered
            && elapsed >= TimeSpan.FromMilliseconds(2500)
            && target?.IsRunning == true
            && ReferenceEquals(selectedVm, target)
            && !powerOperationInProgress)
        {
            await 强制停止长按目标();
        }

        powerLongPressTarget = null;
        if (!powerLongPressTriggered) return;

        // PointerReleased 之后通常还会产生 Click；给它一次消费抑制标记的机会。
        await Task.Delay(120);
        suppressPowerClick = false;
        powerLongPressTriggered = false;
    }

    private async Task 强制停止长按目标()
    {
        powerLongPressTimer?.Stop();
        var vm = powerLongPressTarget;
        if (vm?.IsRunning != true || !ReferenceEquals(selectedVm, vm) || powerOperationInProgress) return;

        powerLongPressTriggered = true;
        suppressPowerClick = true;
        powerOperationInProgress = true;
        try
        {
            var result = sessions.强制停止(vm);
            if (!result.Succeeded) await ShowOperationErrorAsync(result);
        }
        finally
        {
            powerOperationInProgress = false;
            powerLongPressTarget = null;
            await Task.Delay(100);
            RefreshDetails();
        }
    }

    private async void PowerButton_Click(object sender, RoutedEventArgs e)
    {
        powerLongPressTimer?.Stop();
        if (suppressPowerClick)
        {
            suppressPowerClick = false;
            powerLongPressTriggered = false;
            return;
        }
        if (selectedVm is null || powerOperationInProgress) return;

        powerOperationInProgress = true;
        PowerButton.IsEnabled = false;
        try
        {
            if (selectedVm.IsRunning) await 正常关闭仿真(selectedVm);
            else await 启动仿真(selectedVm);
        }
        finally
        {
            powerOperationInProgress = false;
            RefreshDetails();
        }
    }

    private async Task 启动仿真(仿真配置 vm)
    {
        var result = sessions.启动(qemu, vm);
        if (!result.Succeeded)
        {
            await ShowOperationErrorAsync(result);
            return;
        }

        // 立即刷新以建立 D-Bus/D3D11 显示连接；短暂等待仅用于识别 QEMU 早退。
        RefreshDetails();
        await Task.Delay(700);
        if (sessions.存在QMP会话(vm)) return;

        var logPath = Path.Combine(vm.DirPath, "qemu.log");
        var detail = File.Exists(logPath)
            ? string.Join(Environment.NewLine, File.ReadLines(logPath).TakeLast(12))
            : string.Empty;
        await ShowOperationErrorAsync(操作结果.Fail(
            T("session.exitedEarly", "QEMU 启动后立即退出"), detail));
    }

    private async Task 正常关闭仿真(仿真配置 vm)
    {
        VmStatusText.Text = T("session.shuttingDown", "正在等待来宾系统关机…");
        var result = await sessions.关机(vm);
        if (!result.Succeeded)
        {
            await ShowOperationErrorAsync(result);
            return;
        }

        if (await sessions.等待退出(vm, TimeSpan.FromSeconds(12))) return;
        await ShowMessageAsync(
            T("dialog.shutdownTimeoutTitle", "仿真没有响应关机请求"),
            T("main.holdToForceStopHint", "来宾系统仍未关机。请继续等待，或长按电源按钮 2.7 秒强制停止。"));
    }

    private async void PauseResumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is null || !sessions.存在QMP会话(selectedVm)) return;
        var vm = selectedVm;
        PauseResumeButton.IsEnabled = false;
        var status = await sessions.查询运行状态(vm);
        if (!status.结果.Succeeded)
        {
            await ShowOperationErrorAsync(status.结果);
            if (ReferenceEquals(selectedVm, vm))
                PauseResumeButton.IsEnabled = sessions.存在QMP会话(vm);
            return;
        }

        var targetPaused = !string.Equals(status.状态, "paused", StringComparison.OrdinalIgnoreCase);
        var result = targetPaused
            ? await sessions.暂停(vm)
            : await sessions.继续(vm);
        if (!result.Succeeded)
        {
            await ShowOperationErrorAsync(result);
        }
        else if (ReferenceEquals(selectedVm, vm))
        {
            paused = targetPaused;
            更新暂停继续按钮();
            VmStatusText.Text = paused
                ? T("qmp.state.paused", "已暂停")
                : vm.StatusText;
        }
        if (ReferenceEquals(selectedVm, vm))
            PauseResumeButton.IsEnabled = sessions.存在QMP会话(vm);
    }

    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is null || !sessions.存在QMP会话(selectedVm)) return;
        var vm = selectedVm;
        ResetButton.IsEnabled = false;
        var result = await sessions.重置(vm);
        if (!result.Succeeded)
        {
            await ShowOperationErrorAsync(result);
            if (ReferenceEquals(selectedVm, vm)) RefreshDetails();
            return;
        }

        if (ReferenceEquals(selectedVm, vm))
        {
            paused = false;
            更新暂停继续按钮();
        }
        await Task.Delay(900);
        if (!sessions.存在QMP会话(vm))
        {
            var restart = sessions.启动(qemu, vm);
            if (!restart.Succeeded) await ShowOperationErrorAsync(restart);
        }
        if (ReferenceEquals(selectedVm, vm)) RefreshDetails();
    }

    private async void ScreenshotButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is null || !sessions.存在QMP会话(selectedVm)) return;
        var vm = selectedVm;
        ScreenshotButton.IsEnabled = false;
        var directory = Path.Combine(vm.DirPath, "screenshots");
        var path = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
        var result = await sessions.截取屏幕(vm, path);
        if (!result.Succeeded)
        {
            await ShowOperationErrorAsync(result);
        }
        else if (ReferenceEquals(selectedVm, vm))
        {
            VmStatusText.Text = string.Format(T("main.screenshotSaved", "截图已保存至 {0}"), path);
        }
        if (ReferenceEquals(selectedVm, vm))
            ScreenshotButton.IsEnabled = sessions.存在QMP会话(vm);
    }

    private async Task 同步暂停状态(仿真配置 vm)
    {
        var result = await sessions.查询运行状态(vm);
        if (!result.结果.Succeeded || !ReferenceEquals(selectedVm, vm)) return;
        paused = string.Equals(result.状态, "paused", StringComparison.OrdinalIgnoreCase);
        更新暂停继续按钮();
        if (paused) VmStatusText.Text = T("qmp.state.paused", "已暂停");
    }

    private void 更新暂停继续按钮()
    {
        PauseResumeIcon.Glyph = paused ? "\uE768" : "\uE769";
        ToolTipService.SetToolTip(
            PauseResumeButton,
            paused
                ? T("qmp.resume", "继续")
                : T("qmp.pause", "暂停"));
    }
}
