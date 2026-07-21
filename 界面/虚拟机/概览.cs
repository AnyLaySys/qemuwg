using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QemuWG.数据;
using QemuWG.服务;
using QemuWG.界面;
using WinRT.Interop;

namespace QemuWG;

public sealed partial class 主窗
{
    private void VmList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is 虚拟机配置 vm) VmList.SelectedItem = vm;
    }

    private void VmList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        selectedVm = VmList.SelectedItem as 虚拟机配置;
        RefreshDetails();
    }

    private void RefreshDetails()
    {
        ToolsHost.Visibility = Visibility.Collapsed;
        if (selectedVm is null)
        {
            DetailsView.Visibility = Visibility.Collapsed;
            EmptyView.Visibility = Visibility.Visible;
            return;
        }

        EmptyView.Visibility = Visibility.Collapsed;
        DetailsView.Visibility = Visibility.Visible;
        VmNameText.Text = selectedVm.Name;
        VmStatusText.Text = selectedVm.StatusText;
        DisplayBackendText.Text = "VNC";
        DisplayStateText.Text = selectedVm.IsRunning
            ? T("main.displayRunning", "虚拟机显示器已连接")
            : T("main.displayOff", "虚拟机已关机");
        FooterStatusText.Text = selectedVm.StatusText;
        VmPathText.Text = selectedVm.CfgPath;
        StatusDot.Fill = new SolidColorBrush(selectedVm.IsRunning ? ColorHelper.FromArgb(255, 67, 184, 119) : ColorHelper.FromArgb(255, 122, 128, 136));
        StartButton.IsEnabled = !selectedVm.IsRunning;
        EditButton.IsEnabled = !selectedVm.IsRunning;
        ShutdownButton.IsEnabled = selectedVm.IsRunning;
        _ = RefreshDisplayAsync(selectedVm);

        DeviceSummaries.Clear();
        DeviceSummaries.Add(new 设备摘要("\uE950", T("device.processor", "处理器"),
            string.Format(T("device.cpuValue", "{0} 核 · {1}"), selectedVm.CpuCount, RawOrDefault(selectedVm.CpuModel)), ColorHelper.FromArgb(255, 82, 132, 230)));
        DeviceSummaries.Add(new 设备摘要("\uE7F8", T("device.memory", "内存"), FormatMemory(selectedVm.MemoryMb), ColorHelper.FromArgb(255, 70, 173, 101)));
        DeviceSummaries.Add(new 设备摘要("\uE958", T("device.disk", "磁盘"), $"{selectedVm.DiskGb} GB · QCOW2", ColorHelper.FromArgb(255, 224, 154, 54)));
        DeviceSummaries.Add(new 设备摘要("\uE968", T("device.network", "网络"), selectedVm.NetworkMode == "none" ? "none" : $"user · {RawOrDefault(selectedVm.NetworkModel, "auto")}", ColorHelper.FromArgb(255, 44, 169, 172)));
        DeviceSummaries.Add(new 设备摘要("\uE7F4", T("device.display", "显示"), $"vnc · {RawOrDefault(selectedVm.VideoDevice, "auto")}", ColorHelper.FromArgb(255, 161, 98, 215)));
        DeviceSummaries.Add(new 设备摘要("\uE767", T("device.sound", "声卡"), $"{RawOrDefault(selectedVm.AudioDevice, "auto")} · {selectedVm.AudioBackend}", ColorHelper.FromArgb(255, 217, 94, 119)));
        DeviceSummaries.Add(new 设备摘要("\uE8B7", T("device.platform", "平台"), $"{selectedVm.Arch} · {selectedVm.Firmware}", ColorHelper.FromArgb(255, 57, 153, 210)));
        DeviceSummaries.Add(new 设备摘要("\uE8B7", T("device.installMedia", "安装介质"),
            string.IsNullOrWhiteSpace(selectedVm.IsoPath) ? T("device.notConnected", "未连接") : Path.GetFileName(selectedVm.IsoPath), ColorHelper.FromArgb(255, 202, 118, 45)));
        if (selectedVm.Devices.Count > 0)
            DeviceSummaries.Add(new 设备摘要("\uE950", T("device.additional", "附加设备"),
                string.Format(T("device.additionalValue", "{0} 个 · {1}"), selectedVm.Devices.Count,
                    string.Join(", ", selectedVm.Devices.Take(3).Select(device => device.Model))),
                ColorHelper.FromArgb(255, 112, 121, 214)));
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is null) return;
        var vm = selectedVm;
        StartButton.IsEnabled = false;
        var result = sessions.Start(qemu, selectedVm);
        if (!result.Succeeded)
        {
            await ShowOperationErrorAsync(result);
            RefreshDetails();
            return;
        }
        await Task.Delay(700);
        if (!sessions.HasQmpSession(vm))
        {
            var logPath = Path.Combine(vm.DirPath, "qemu.log");
            var detail = File.Exists(logPath)
                ? string.Join(Environment.NewLine, File.ReadLines(logPath).TakeLast(12))
                : string.Empty;
            await ShowOperationErrorAsync(操作结果.Fail(
                T("session.exitedEarly", "QEMU 启动后立即退出"), detail));
        }
        RefreshDetails();
    }

    private async void ShutdownButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is null) return;
        var vm = selectedVm;
        ShutdownButton.IsEnabled = false;
        VmStatusText.Text = T("session.shuttingDown", "正在等待来宾系统关机…");
        FooterStatusText.Text = VmStatusText.Text;
        var result = await sessions.ShutdownAsync(vm);
        if (!result.Succeeded)
        {
            await ShowOperationErrorAsync(result);
            RefreshDetails();
            return;
        }

        if (await sessions.WaitForExitAsync(vm, TimeSpan.FromSeconds(12)))
        {
            RefreshDetails();
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = RootXamlRoot,
            Title = T("dialog.shutdownTimeoutTitle", "虚拟机没有响应关机请求"),
            Content = T("dialog.shutdownTimeoutMessage", "来宾系统可能尚未启动、未启用 ACPI，或正在处理关机。可以继续等待，也可以强制停止。"),
            PrimaryButtonText = T("main.forceStop", "强制停止"),
            CloseButtonText = T("dialog.keepWaiting", "继续等待"),
            DefaultButton = ContentDialogButton.Close
        };
        if (await ShowDialogAsync(confirm) == ContentDialogResult.Primary)
        {
            var stopResult = sessions.ForceStop(vm);
            if (!stopResult.Succeeded) await ShowOperationErrorAsync(stopResult);
        }
        RefreshDetails();
    }

    private async void ForceStop_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is null || !selectedVm.IsRunning) return;
        var confirm = new ContentDialog
        {
            XamlRoot = RootXamlRoot,
            Title = T("dialog.forceStopTitle", "强制停止虚拟机？"),
            Content = T("dialog.forceStopMessage", "未保存的数据可能丢失。"),
            PrimaryButtonText = T("main.forceStop", "强制停止"),
            CloseButtonText = T("common.cancel", "取消"),
            DefaultButton = ContentDialogButton.Close
        };
        if (await ShowDialogAsync(confirm) != ContentDialogResult.Primary) return;
        var result = sessions.ForceStop(selectedVm);
        if (!result.Succeeded) await ShowOperationErrorAsync(result);
    }

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is null || selectedVm.IsRunning) return;
        var dialog = new 虚拟机编辑(WindowNative.GetWindowHandle(this), qemu, qemuSvc, vmRepo.RootPath, selectedVm)
        {
            XamlRoot = RootXamlRoot
        };
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;

        var edited = dialog.BuildMachine();
        CopyEditableValues(edited, selectedVm);
        var result = await vmRepo.UpdateAsync(selectedVm);
        if (!result.Succeeded) await ShowOperationErrorAsync(result);
        VmList.ItemsSource = null;
        VmList.ItemsSource = Machines;
        VmList.SelectedItem = selectedVm;
        RefreshDetails();
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is null) return;
        if (selectedVm.IsRunning)
        {
            await ShowMessageAsync(T("dialog.cannotDeleteTitle", "无法删除"), T("dialog.closeVmFirst", "请先关闭虚拟机。"));
            return;
        }

        var vm = selectedVm;
        var confirm = new ContentDialog
        {
            XamlRoot = RootXamlRoot,
            Title = string.Format(T("dialog.deleteTitle", "删除“{0}”？"), vm.Name),
            Content = T("dialog.deleteMessage", "虚拟机目录将移至回收站。"),
            PrimaryButtonText = T("main.deleteVm", "删除虚拟机"),
            CloseButtonText = T("common.cancel", "取消"),
            DefaultButton = ContentDialogButton.Close
        };
        if (await ShowDialogAsync(confirm) != ContentDialogResult.Primary) return;

        var result = await vmRepo.DeleteAsync(vm);
        if (!result.Succeeded)
        {
            await ShowOperationErrorAsync(result);
            return;
        }
        Machines.Remove(vm);
        selectedVm = null;
        if (Machines.Count > 0) VmList.SelectedIndex = 0;
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
            await ShowMessageAsync(T("dialog.noLogTitle", "暂无日志"), T("dialog.noLogMessage", "该虚拟机还没有生成 QEMU 日志。"));
            return;
        }
        OpenPath(log);
    }

    private async void DiskManager_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is null) return;
        await ShowDiskManagerAsync(selectedVm);
    }

    private async void QmpConsole_Click(object sender, RoutedEventArgs e)
    {
        应用日志.Write("Opening 虚拟机控制");
        if (selectedVm is null) return;
        if (!sessions.HasQmpSession(selectedVm))
        {
            await ShowMessageAsync(
                T("qmp.title", "QMP 控制台"),
                T("qmp.requiresRunning", "QMP 控制台仅在虚拟机运行时可用。"));
            return;
        }

        try
        {
            var dialog = new 虚拟机控制(WindowNative.GetWindowHandle(this), qemu, sessions, selectedVm) { XamlRoot = RootXamlRoot };
            应用日志.Write("虚拟机控制 constructed");
            await ShowDialogAsync(dialog);
            应用日志.Write("虚拟机控制 closed");
            RefreshDetails();
        }
        catch (Exception exception)
        {
            应用日志.Write("虚拟机控制 failed: " + exception);
            await ShowMessageAsync(
                T("dialog.operationFailed", "操作失败"),
                string.Format(T("qmp.openFailed", "无法打开 QMP 控制台：{0}"), exception.Message));
        }
    }

    private async void GuestAgent_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is null) return;
        if (!sessions.HasQmpSession(selectedVm))
        {
            await ShowMessageAsync(T("guestAgent.title", "Guest Agent"), T("guestAgent.requiresRunning", "Guest Agent 仅在虚拟机运行时可用。"));
            return;
        }
        try
        {
            var dialog = new 来宾代理界面(WindowNative.GetWindowHandle(this), sessions, selectedVm) { XamlRoot = RootXamlRoot };
            await ShowDialogAsync(dialog);
        }
        catch (Exception exception)
        {
            应用日志.Write("来宾代理界面 failed: " + exception);
            await ShowMessageAsync(
                T("dialog.operationFailed", "操作失败"),
                string.Format(T("guestAgent.openFailed", "无法打开 Guest Agent：{0}"), exception.Message));
        }
    }

}
