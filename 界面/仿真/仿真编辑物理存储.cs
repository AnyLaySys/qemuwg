using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.数据;
using QemuWG.服务;

namespace QemuWG.界面;

public sealed partial class 仿真编辑
{
    private async Task LoadPhysicalStorageAsync()
    {
        PhysicalStorageStatus.Text = T("vmEditor.physicalStorageScanning", "正在扫描物理磁盘和分区…");
        var devices = await qemuSvc.获取物理存储();
        PhysicalStorageCombo.ItemsSource = devices;
        PhysicalStorageStatus.Text = devices.Count == 0
            ? T("vmEditor.physicalStorageUnavailable", "未发现物理磁盘或分区。")
            : string.Format(T("vmEditor.physicalStorageCount", "发现 {0} 个物理磁盘或分区。"), devices.Count);
    }

    private void AddPhysicalStorage_Click(object sender, RoutedEventArgs e)
    {
        if (PhysicalStorageCombo.SelectedItem is not 物理存储设备 device) return;
        var storage = new 物理存储挂载
        {
            DevicePath = device.DevicePath,
            DisplayName = device.DisplayName,
            Interface = (PhysicalStorageInterfaceCombo.SelectedItem?.ToString() ?? "virtio").Trim(),
            ReadOnly = !string.Equals(
                (PhysicalStorageAccessCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
                "readwrite",
                StringComparison.OrdinalIgnoreCase),
            Kind = device.Kind,
            DiskNumber = device.DiskNumber,
            PartitionNumber = device.PartitionNumber,
            Offset = device.Offset,
            Size = device.Size,
            UniqueId = device.UniqueId
        };
        if (ConfiguredPhysicalStorage.Any(item => 物理存储冲突检查.互相冲突(item, storage)))
        {
            PhysicalStorageStatus.Text = T("session.physicalStorageConflict", "物理磁盘、分区或重叠范围不能重复挂载。");
            return;
        }
        ConfiguredPhysicalStorage.Add(storage);
    }

    private void RemovePhysicalStorage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: 物理存储挂载 storage }) ConfiguredPhysicalStorage.Remove(storage);
    }
}
