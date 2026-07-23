using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.数据;
using QemuWG.服务;
using QemuWG.界面;
using WinRT.Interop;

namespace QemuWG;

public sealed partial class 主窗
{
    private void RefreshDetails()
    {
        if (selectedVm is null)
        {
            var animateEmpty = EmptyView.Visibility != Visibility.Visible;
            DetailsView.Visibility = Visibility.Collapsed;
            EmptyView.Visibility = Visibility.Visible;
            VmFeaturesHost.Content = null;
            VmFeaturesPane.Visibility = Visibility.Collapsed;
            DisplayPane.Visibility = Visibility.Visible;
            lastAnimatedVmId = null;
            if (animateEmpty) _ = 页面过渡动画.渐进显示(EmptyView, 9);
            return;
        }

        var vmChanged = !string.Equals(lastAnimatedVmId, selectedVm.Id, StringComparison.Ordinal);
        var animateDetails = DetailsView.Visibility != Visibility.Visible
                             || vmChanged;
        EmptyView.Visibility = Visibility.Collapsed;
        DetailsView.Visibility = Visibility.Visible;
        VmNameText.Text = selectedVm.Name;
        VmStatusText.Text = selectedVm.StatusText;
        var useEmbeddedDisplay = string.Equals(selectedVm.DisplayBackend, "dbus", StringComparison.OrdinalIgnoreCase);
        DisplayHeader.Visibility = selectedVm.IsRunning && useEmbeddedDisplay
            ? Visibility.Collapsed
            : Visibility.Visible;
        DisplayBackendText.Text = useEmbeddedDisplay ? "D-Bus · D3D11" : selectedVm.DisplayBackend.ToUpperInvariant();
        DetachDisplayButton.Visibility = useEmbeddedDisplay ? Visibility.Collapsed : Visibility.Visible;
        DisplayStateText.Text = !selectedVm.IsRunning
            ? T("main.displayOff", "仿真已关机")
            : useEmbeddedDisplay
                ? T("main.displayConnecting", "正在连接仿真显示器…")
                : T("main.displayNativeWindow", "仿真正在 QEMU 原生窗口中显示");
        PowerButton.IsEnabled = !powerOperationInProgress;
        PowerButtonIcon.Glyph = "\uE7E8";
        ToolTipService.SetToolTip(
            PowerButton,
            selectedVm.IsRunning
                ? T("main.shutdown", "关机") + " · " + T("main.holdToForceStop", "长按 2.7 秒强制停止")
                : T("main.start", "启动"));
        EditButton.IsEnabled = !selectedVm.IsRunning;
        DeleteVmMenuItem.IsEnabled = !selectedVm.IsRunning;
        VmSnapshotButton.IsEnabled = true;
        VmDiskManagerButton.IsEnabled = true;
        VmFeaturesButton.IsEnabled = true;
        var hasQmp = selectedVm.IsRunning && sessions.存在QMP会话(selectedVm);
        PauseResumeButton.IsEnabled = hasQmp;
        ResetButton.IsEnabled = hasQmp;
        ScreenshotButton.IsEnabled = hasQmp;
        if (!selectedVm.IsRunning)
        {
            PowerSecondaryButtons.Visibility = Visibility.Collapsed;
            powerSecondaryButtonsVisible = false;
            paused = false;
            更新暂停继续按钮();
        }
        else if (hasQmp)
        {
            _ = 同步暂停状态(selectedVm);
        }
        OpenVmFolderMenuItem.IsEnabled = Directory.Exists(selectedVm.DirPath);
        OpenQemuLogMenuItem.IsEnabled = File.Exists(Path.Combine(selectedVm.DirPath, "qemu.log"));
        if (VmFeaturesPane.Visibility == Visibility.Visible)
        {
            if (vmChanged) 显示仿真功能(selectedVm);
            else VmFeaturesContextText.Text = $"{selectedVm.Name} · {selectedVm.StatusText}";
        }
        _ = RefreshDisplayAsync(selectedVm);

        var summaries = new List<设备摘要>
        {
            new("\uE950", T("device.processor", "处理器"),
                string.Format(T("device.cpuValue", "{0} 核 · {1}"), selectedVm.CpuCount, RawOrDefault(selectedVm.CpuModel)), ColorHelper.FromArgb(255, 82, 132, 230)),
            new("\uE7F8", T("device.memory", "内存"), FormatMemory(selectedVm.MemoryMb), ColorHelper.FromArgb(255, 70, 173, 101)),
            new("\uE958", T("device.disk", "磁盘"), $"{selectedVm.DiskGb} GB · {RawOrDefault(selectedVm.DiskFormat, "qcow2").ToUpperInvariant()}", ColorHelper.FromArgb(255, 224, 154, 54)),
            new("\uE968", T("device.network", "网络"), selectedVm.NetworkMode == "none" ? "none" : $"user · {RawOrDefault(selectedVm.NetworkModel, "auto")}", ColorHelper.FromArgb(255, 44, 169, 172)),
            new("\uE7F4", T("device.display", "显示"), $"{selectedVm.DisplayBackend} · {RawOrDefault(selectedVm.VideoDevice, "auto")}", ColorHelper.FromArgb(255, 161, 98, 215)),
            new("\uE767", T("device.sound", "声卡"), $"{RawOrDefault(selectedVm.AudioDevice, "auto")} · {selectedVm.AudioBackend}", ColorHelper.FromArgb(255, 217, 94, 119)),
            new("\uE912", T("device.platform", "平台"), $"{selectedVm.Arch} · {selectedVm.Firmware}", ColorHelper.FromArgb(255, 57, 153, 210)),
            new("\uE8A5", T("device.installMedia", "安装介质"),
                string.IsNullOrWhiteSpace(selectedVm.IsoPath) ? T("device.notConnected", "未连接") : Path.GetFileName(selectedVm.IsoPath), ColorHelper.FromArgb(255, 202, 118, 45))
        };
        if (selectedVm.Devices.Count > 0)
            summaries.Add(new 设备摘要("\uE772", T("device.additional", "附加设备"),
                string.Format(T("device.additionalValue", "{0} 个 · {1}"), selectedVm.Devices.Count,
                    string.Join(", ", selectedVm.Devices.Take(3).Select(device => device.Model))),
                ColorHelper.FromArgb(255, 112, 121, 214)));
        for (var index = 0; index < summaries.Count; index++)
        {
            if (index < DeviceSummaries.Count) DeviceSummaries[index] = summaries[index];
            else DeviceSummaries.Add(summaries[index]);
        }
        while (DeviceSummaries.Count > summaries.Count) DeviceSummaries.RemoveAt(DeviceSummaries.Count - 1);
        lastAnimatedVmId = selectedVm.Id;
        if (animateDetails)
        {
            _ = 页面过渡动画.渐进显示(VmNameText, 4);
            _ = 页面过渡动画.渐进显示(VmStatusText, 4);
            _ = 页面过渡动画.渐进显示(VmSummaryColumn, 6);
            if (DisplayFallback.Visibility == Visibility.Visible)
                _ = 页面过渡动画.渐进显示(DisplayFallback, 6);
        }
    }

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is null) return;
        var dialog = new 仿真编辑(WindowNative.GetWindowHandle(this), qemu, qemuSvc, vmRepo.根目录, selectedVm)
        {
            XamlRoot = RootXamlRoot
        };
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;

        var edited = dialog.BuildMachine();
        CopyEditableValues(edited, selectedVm);
        var result = await vmRepo.更新(selectedVm);
        if (!result.Succeeded) await ShowOperationErrorAsync(result);
        if (vmFeatureViews.Remove(selectedVm.Id, out var previousFeatureView)
            && ReferenceEquals(VmFeaturesHost.Content, previousFeatureView))
        {
            VmFeaturesHost.Content = null;
            if (VmFeaturesPane.Visibility == Visibility.Visible) 显示仿真功能(selectedVm);
        }
        刷新并重排仿真(selectedVm);
        RefreshDetails();
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is null) return;
        if (selectedVm.IsRunning)
        {
            await ShowMessageAsync(T("dialog.cannotDeleteTitle", "无法删除"), T("dialog.closeVmFirst", "请先关闭仿真。"));
            return;
        }

        var vm = selectedVm;
        var confirm = new ContentDialog
        {
            XamlRoot = RootXamlRoot,
            Title = string.Format(T("dialog.deleteTitle", "删除“{0}”？"), vm.Name),
            Content = T("dialog.deleteMessage", "仿真目录将移至回收站。"),
            PrimaryButtonText = T("main.deleteVm", "删除仿真"),
            CloseButtonText = T("common.cancel", "取消"),
            DefaultButton = ContentDialogButton.Close
        };
        if (await ShowDialogAsync(confirm) != ContentDialogResult.Primary) return;

        toolSessions.停止作用域(vm.Id);
        var result = await vmRepo.删除(vm);
        if (!result.Succeeded)
        {
            await ShowOperationErrorAsync(result);
            return;
        }
        移除仿真(vm);
        if (vmFeatureViews.Remove(vm.Id, out var removedView) && ReferenceEquals(VmFeaturesHost.Content, removedView))
            VmFeaturesHost.Content = null;
        selectedVm = null;
        if (仿真侧栏项列表.Count > 0) 选择仿真(仿真侧栏项列表[0]);
        else RefreshDetails();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is not null) OpenPath(selectedVm.DirPath);
    }

    private async void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is null) return;
        var log = Path.Combine(selectedVm.DirPath, "qemu.log");
        if (!File.Exists(log))
        {
            await ShowMessageAsync(T("dialog.noLogTitle", "暂无日志"), T("dialog.noLogMessage", "该仿真还没有生成 QEMU 日志。"));
            return;
        }
        OpenPath(log);
    }

    private async void DiskManager_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is null) return;
        await ShowDiskManagerAsync(selectedVm);
    }

    private async void SnapshotManager_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is null) return;
        try
        {
            var dialog = new 快照管理器(WindowNative.GetWindowHandle(this), qemu, sessions, selectedVm)
            {
                XamlRoot = RootXamlRoot
            };
            await ShowDialogAsync(dialog);
            RefreshDetails();
        }
        catch (Exception exception)
        {
            应用日志.写("Snapshot manager failed: " + exception);
            await ShowMessageAsync(
                T("dialog.operationFailed", "操作失败"),
                string.Format(T("snapshot.openFailed", "无法打开快照管理器：{0}"), exception.Message));
        }
    }

    private void VmFeaturesButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is null) return;
        显示仿真功能(selectedVm);
        VmFeaturesPane.Visibility = Visibility.Visible;
        _ = 页面过渡动画.渐进显示(VmFeaturesPane, 9);
    }

    private void BackToDisplayButton_Click(object sender, RoutedEventArgs e)
    {
        页面过渡动画.渐进隐藏(VmFeaturesPane, 6);
    }

    private void 显示仿真功能(仿真配置 vm)
    {
        if (!vmFeatureViews.TryGetValue(vm.Id, out var view))
        {
            view = new QEMU附加功能(WindowNative.GetWindowHandle(this), qemu, toolSessions, vm);
            vmFeatureViews.Add(vm.Id, view);
            按钮交互动画.启用(view);
        }
        VmFeaturesContextText.Text = $"{vm.Name} · {vm.StatusText}";
        VmFeaturesHost.Content = view;
        if (VmFeaturesPane.Visibility == Visibility.Visible) _ = 页面过渡动画.渐进显示(view, 6);
    }

    private async void DetachDisplayButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is null || !selectedVm.IsRunning) return;
        var result = sessions.分离显示(selectedVm);
        if (!result.Succeeded) await ShowOperationErrorAsync(result);
    }

}
