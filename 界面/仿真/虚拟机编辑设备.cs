using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.数据;
using QemuWG.服务;

namespace QemuWG.界面;

public sealed partial class 虚拟机编辑
{
    private void AddDevice_Click(object sender, RoutedEventArgs e)
    {
        var model = DeviceModelCombo.SelectedItem?.ToString() ?? DeviceModelCombo.Text.Trim();
        if (string.IsNullOrWhiteSpace(model)) return;
        var device = new QEMU设备 { Model = model };
        ConfiguredDevices.Add(device);
        if (availableInputDeviceModels.Contains(device.Model)) ConfiguredInputDevices.Add(device);
        DeviceList.SelectedItem = device;
        RemoveDeviceButton.IsEnabled = true;
    }

    private void RemoveDevice_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not QEMU设备 device) return;
        ConfiguredDevices.Remove(device);
        ConfiguredInputDevices.Remove(device);
        if (ReferenceEquals(DeviceList.SelectedItem, device))
        {
            SelectedDeviceProperties.Clear();
            DevicePropertyCombo.ItemsSource = null;
            RemoveDeviceButton.IsEnabled = false;
            RemoveDevicePropertyButton.IsEnabled = false;
        }
    }

    private async void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedDeviceProperties.Clear();
        RemoveDeviceButton.IsEnabled = DeviceList.SelectedItem is QEMU设备;
        RemoveDevicePropertyButton.IsEnabled = false;
        if (DeviceList.SelectedItem is not QEMU设备 device || ArchCombo.SelectedItem is not QEMU架构 arch) return;
        foreach (var property in device.Properties) SelectedDeviceProperties.Add(new QEMU选项 { Name = property.Key, Value = property.Value });
        DevicePropertyHintText.Text = T("vmEditor.devicePropertiesLoading", "正在读取设备属性…");
        try
        {
            availableDeviceProperties = await qemuSvc.获取设备属性(arch, device.Model);
            DevicePropertyCombo.ItemsSource = availableDeviceProperties;
            DevicePropertyHintText.Text = string.Format(T("vmEditor.devicePropertyCount", "{0} 个可配置属性"), availableDeviceProperties.Count);
        }
        catch (Exception exception)
        {
            availableDeviceProperties = [];
            DevicePropertyCombo.ItemsSource = null;
            DevicePropertyHintText.Text = exception.Message;
        }
    }

    private void DevicePropertyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DevicePropertyCombo.SelectedItem is not QEMU设备属性 property) return;
        DevicePropertyValueCombo.ItemsSource = property.Choices;
        DevicePropertyValueCombo.Text = property.DefaultValue;
        DevicePropertyHintText.Text = string.IsNullOrWhiteSpace(property.Description) ? $"{property.Name} <{property.Type}>" : property.Description;
    }

    private void AddDeviceProperty_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not QEMU设备 device) return;
        var name = DevicePropertyCombo.SelectedItem is QEMU设备属性 definition ? definition.Name : DevicePropertyCombo.Text.Trim();
        var value = DevicePropertyValueCombo.Text.Trim();
        if (name.Length == 0 || value.Length == 0) return;
        device.SetProperty(name, value);
        var existing = SelectedDeviceProperties.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) SelectedDeviceProperties.Remove(existing);
        SelectedDeviceProperties.Add(new QEMU选项 { Name = name, Value = value });
        DevicePropertyValueCombo.Text = string.Empty;
    }

    private void RemoveDeviceProperty_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not QEMU设备 device || DevicePropertyList.SelectedItem is not QEMU选项 property) return;
        device.RemoveProperty(property.Name);
        SelectedDeviceProperties.Remove(property);
        RemoveDevicePropertyButton.IsEnabled = false;
    }

    private void DevicePropertyList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RemoveDevicePropertyButton.IsEnabled = DevicePropertyList.SelectedItem is QEMU选项;

}
