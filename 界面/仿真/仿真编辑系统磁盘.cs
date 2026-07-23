using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.数据;
using QemuWG.服务;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace QemuWG.界面;

public sealed partial class 仿真编辑
{
    private readonly 系统磁盘检查 systemDiskService = new();
    private 磁盘镜像信息? selectedSystemDiskInfo;
    private string validatedSystemDiskPath = string.Empty;

    private void 初始化系统磁盘(仿真配置 vm)
    {
        DiskBox.Value = vm.DiskGb;
        if (source is null)
        {
            SystemDiskModeCombo.SelectedIndex = -1;
            SystemDiskPathBox.Text = string.Empty;
        }
        else
        {
            SelectTaggedItem(SystemDiskModeCombo, 系统磁盘模式.已有);
            SystemDiskModeCombo.IsEnabled = false;
            SystemDiskPathBox.Text = vm.DiskPath;
        }
        更新系统磁盘字段();
    }

    private void SystemDiskModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => 更新系统磁盘字段();

    private void 更新系统磁盘字段()
    {
        var mode = SelectedTag(SystemDiskModeCombo, string.Empty);
        NewSystemDiskField.Visibility = mode == 系统磁盘模式.新建 ? Visibility.Visible : Visibility.Collapsed;
        ExistingSystemDiskField.Visibility = mode == 系统磁盘模式.已有 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void BrowseSystemDisk_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, ownerHandle);
        foreach (var extension in new[] { ".qcow2", ".qcow", ".raw", ".img", ".vmdk", ".vdi", ".vhd", ".vhdx" })
            picker.FileTypeFilter.Add(extension);
        var file = await picker.PickSingleFileAsync();
        if (file is not null) SystemDiskPathBox.Text = file.Path;
    }

    private async void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var error = ValidateInput() ?? await 验证系统磁盘();
            if (error is null) return;
            args.Cancel = true;
            ValidationInfo.Message = error;
            ValidationInfo.IsOpen = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task<string?> 验证系统磁盘()
    {
        selectedSystemDiskInfo = null;
        validatedSystemDiskPath = string.Empty;
        var mode = SelectedTag(SystemDiskModeCombo, string.Empty);
        if (mode == 系统磁盘模式.新建)
            return !double.IsFinite(DiskBox.Value) || DiskBox.Value < 1
                ? T("vmEditor.validation.disk", "磁盘至少为 1 GB。")
                : null;
        if (mode != 系统磁盘模式.已有)
            return T("vmEditor.validation.diskMode", "请选择新建磁盘或使用已有磁盘。");

        var checkedDisk = await systemDiskService.检查(install, SystemDiskPathBox.Text);
        if (!checkedDisk.结果.Succeeded) return checkedDisk.结果.Message;
        validatedSystemDiskPath = checkedDisk.路径;
        selectedSystemDiskInfo = checkedDisk.信息;
        return null;
    }

    private void 读取系统磁盘(仿真配置 vm)
    {
        var mode = SelectedTag(SystemDiskModeCombo, string.Empty);
        vm.DiskMode = mode;
        if (mode == 系统磁盘模式.新建)
        {
            vm.DiskPath = string.Empty;
            vm.DiskFormat = "qcow2";
            vm.DiskGb = (int)DiskBox.Value;
            return;
        }

        vm.DiskPath = validatedSystemDiskPath.Length > 0
            ? validatedSystemDiskPath
            : SystemDiskPathBox.Text.Trim();
        if (selectedSystemDiskInfo is null) return;
        vm.DiskFormat = selectedSystemDiskInfo.Format;
        vm.DiskGb = 系统磁盘检查.转换容量(selectedSystemDiskInfo.VirtualSize);
    }
}
