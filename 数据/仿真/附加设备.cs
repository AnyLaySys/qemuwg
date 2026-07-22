namespace QemuWG.数据;

public sealed class QEMU设备 : System.ComponentModel.INotifyPropertyChanged
{
    public string Model { get; set; } = string.Empty;
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string DisplayText => Properties.Count == 0
        ? Model
        : $"{Model}," + string.Join(',', Properties.Select(item => $"{item.Key}={item.Value}"));

    public void SetProperty(string name, string value)
    {
        Properties[name] = value;
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DisplayText)));
    }

    public void RemoveProperty(string name)
    {
        if (Properties.Remove(name)) PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DisplayText)));
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
