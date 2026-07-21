using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.数据;

namespace QemuWG.界面;

public sealed partial class 虚拟机编辑
{
    private void AddInputDevice_Click(object sender, RoutedEventArgs e)
    {
        var model = InputDeviceModelCombo.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(model)) return;

        var device = new QEMU设备 { Model = model };
        ConfiguredDevices.Add(device);
        ConfiguredInputDevices.Add(device);
        InputDeviceModelCombo.SelectedItem = null;
    }

    private void RemoveInputDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: QEMU设备 device }) return;
        ConfiguredInputDevices.Remove(device);
        ConfiguredDevices.Remove(device);
    }

    private void RefreshConfiguredInputDevices()
    {
        ConfiguredInputDevices.Clear();
        foreach (var device in ConfiguredDevices)
        {
            if (availableInputDeviceModels.Contains(device.Model)) ConfiguredInputDevices.Add(device);
        }
    }
}
