using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.data;
using QemuWG.svc;

namespace QemuWG.ui;

public sealed partial class VmEditorDialog
{
    private void AddDevice_Click(object sender, RoutedEventArgs e)
    {
        var model = DeviceModelCombo.SelectedItem?.ToString() ?? DeviceModelCombo.Text.Trim();
        if (string.IsNullOrWhiteSpace(model)) return;
        var device = new QemuDeviceEntry { Model = model };
        ConfiguredDevices.Add(device);
        DeviceList.SelectedItem = device;
        RemoveDeviceButton.IsEnabled = true;
    }

    private void RemoveDevice_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not QemuDeviceEntry device) return;
        ConfiguredDevices.Remove(device);
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
        RemoveDeviceButton.IsEnabled = DeviceList.SelectedItem is QemuDeviceEntry;
        RemoveDevicePropertyButton.IsEnabled = false;
        if (DeviceList.SelectedItem is not QemuDeviceEntry device || ArchCombo.SelectedItem is not QemuArch arch) return;
        foreach (var property in device.Properties) SelectedDeviceProperties.Add(new QemuOptionEntry { Name = property.Key, Value = property.Value });
        DevicePropertyHintText.Text = T("vmEditor.devicePropertiesLoading", "正在读取设备属性…");
        try
        {
            availableDeviceProperties = await qemuSvc.GetDevicePropertiesAsync(arch, device.Model);
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
        if (DevicePropertyCombo.SelectedItem is not QemuDevicePropDef property) return;
        DevicePropertyValueCombo.ItemsSource = property.Choices;
        DevicePropertyValueCombo.Text = property.DefaultValue;
        DevicePropertyHintText.Text = string.IsNullOrWhiteSpace(property.Description) ? $"{property.Name} <{property.Type}>" : property.Description;
    }

    private void AddDeviceProperty_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not QemuDeviceEntry device) return;
        var name = DevicePropertyCombo.SelectedItem is QemuDevicePropDef definition ? definition.Name : DevicePropertyCombo.Text.Trim();
        var value = DevicePropertyValueCombo.Text.Trim();
        if (name.Length == 0 || value.Length == 0) return;
        device.SetProperty(name, value);
        var existing = SelectedDeviceProperties.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) SelectedDeviceProperties.Remove(existing);
        SelectedDeviceProperties.Add(new QemuOptionEntry { Name = name, Value = value });
        DevicePropertyValueCombo.Text = string.Empty;
    }

    private void RemoveDeviceProperty_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not QemuDeviceEntry device || DevicePropertyList.SelectedItem is not QemuOptionEntry property) return;
        device.RemoveProperty(property.Name);
        SelectedDeviceProperties.Remove(property);
        RemoveDevicePropertyButton.IsEnabled = false;
    }

    private void DevicePropertyList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RemoveDevicePropertyButton.IsEnabled = DevicePropertyList.SelectedItem is QemuOptionEntry;

}





