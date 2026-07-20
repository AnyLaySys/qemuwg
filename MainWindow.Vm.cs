using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QemuWG.data;
using QemuWG.svc;
using QemuWG.ui;
using WinRT.Interop;

namespace QemuWG;

public sealed partial class MainWindow
{
    private void VmList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is VmCfg vm) VmList.SelectedItem = vm;
    }

    private void VmList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        selectedVm = VmList.SelectedItem as VmCfg;
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
        ConsoleBackendText.Text = selectedVm.DisplayBackend.ToUpperInvariant();
        ConsoleStateText.Text = selectedVm.IsRunning
            ? T("main.consoleRunning", "QEMU 显示窗口已启动")
            : T("main.consoleOff", "虚拟机已关机");
        FooterStatusText.Text = selectedVm.StatusText;
        VmPathText.Text = selectedVm.CfgPath;
        StatusDot.Fill = new SolidColorBrush(selectedVm.IsRunning ? ColorHelper.FromArgb(255, 67, 184, 119) : ColorHelper.FromArgb(255, 122, 128, 136));
        StartButton.IsEnabled = !selectedVm.IsRunning;
        EditButton.IsEnabled = !selectedVm.IsRunning;
        ShutdownButton.IsEnabled = selectedVm.IsRunning;

        DeviceSummaries.Clear();
        DeviceSummaries.Add(new DeviceSummary("\uE950", T("device.processor", "处理器"),
            string.Format(T("device.cpuValue", "{0} 核 · {1}"), selectedVm.CpuCount, RawOrDefault(selectedVm.CpuModel)), ColorHelper.FromArgb(255, 82, 132, 230)));
        DeviceSummaries.Add(new DeviceSummary("\uE7F8", T("device.memory", "内存"), FormatMemory(selectedVm.MemoryMb), ColorHelper.FromArgb(255, 70, 173, 101)));
        DeviceSummaries.Add(new DeviceSummary("\uE958", T("device.disk", "磁盘"), $"{selectedVm.DiskGb} GB · QCOW2", ColorHelper.FromArgb(255, 224, 154, 54)));
        DeviceSummaries.Add(new DeviceSummary("\uE968", T("device.network", "网络"), selectedVm.NetworkMode == "none" ? "none" : $"user · {RawOrDefault(selectedVm.NetworkModel, "auto")}", ColorHelper.FromArgb(255, 44, 169, 172)));
        DeviceSummaries.Add(new DeviceSummary("\uE7F4", T("device.display", "显示"), $"{selectedVm.DisplayBackend} · {RawOrDefault(selectedVm.VideoDevice, "auto")}", ColorHelper.FromArgb(255, 161, 98, 215)));
        DeviceSummaries.Add(new DeviceSummary("\uE767", T("device.sound", "声卡"), $"{RawOrDefault(selectedVm.AudioDevice, "auto")} · {selectedVm.AudioBackend}", ColorHelper.FromArgb(255, 217, 94, 119)));
        DeviceSummaries.Add(new DeviceSummary("\uE8B7", T("device.platform", "平台"), $"{selectedVm.Arch} · {selectedVm.Firmware}", ColorHelper.FromArgb(255, 57, 153, 210)));
        DeviceSummaries.Add(new DeviceSummary("\uE8B7", T("device.installMedia", "安装介质"),
            string.IsNullOrWhiteSpace(selectedVm.IsoPath) ? T("device.notConnected", "未连接") : Path.GetFileName(selectedVm.IsoPath), ColorHelper.FromArgb(255, 202, 118, 45)));
        if (selectedVm.Devices.Count > 0)
            DeviceSummaries.Add(new DeviceSummary("\uE950", T("device.additional", "附加设备"),
                string.Format(T("device.additionalValue", "{0} 个 · {1}"), selectedVm.Devices.Count,
                    string.Join(", ", selectedVm.Devices.Take(3).Select(device => device.Model))),
                ColorHelper.FromArgb(255, 112, 121, 214)));
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is null) return;
        var result = sessions.Start(qemu, selectedVm);
        if (!result.Succeeded) await ShowOperationErrorAsync(result);
        RefreshDetails();
    }

    private async void ShutdownButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is null) return;
        var result = await sessions.ShutdownAsync(selectedVm);
        if (!result.Succeeded) await ShowOperationErrorAsync(result);
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
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        var result = sessions.ForceStop(selectedVm);
        if (!result.Succeeded) await ShowOperationErrorAsync(result);
    }

    private async void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedVm is null || selectedVm.IsRunning) return;
        var dialog = new VmEditorDialog(WindowNative.GetWindowHandle(this), qemu, qemuSvc, vmRepo.RootPath, selectedVm)
        {
            XamlRoot = RootXamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

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
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

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
            var dialog = new VmControlDialog(WindowNative.GetWindowHandle(this), sessions, selectedVm) { XamlRoot = RootXamlRoot };
            await dialog.ShowAsync();
            RefreshDetails();
        }
        catch (Exception exception)
        {
            AppLog.Write("VmControlDialog failed: " + exception);
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
            var dialog = new GuestMgrDialog(WindowNative.GetWindowHandle(this), sessions, selectedVm) { XamlRoot = RootXamlRoot };
            await dialog.ShowAsync();
        }
        catch (Exception exception)
        {
            AppLog.Write("GuestMgrDialog failed: " + exception);
            await ShowMessageAsync(
                T("dialog.operationFailed", "操作失败"),
                string.Format(T("guestAgent.openFailed", "无法打开 Guest Agent：{0}"), exception.Message));
        }
    }

}


