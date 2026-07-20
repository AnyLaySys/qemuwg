namespace QemuWG.data;

public sealed record QemuArch(string Id, string DisplayName, string ExecutablePath);

public sealed class QemuCmdOptionDef
{
    public string Name { get; set; } = string.Empty;
    public string Syntax { get; set; } = string.Empty;
}

public sealed class QemuOptionEntry
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string DisplayText => string.IsNullOrWhiteSpace(Value) ? $"-{Name}" : $"-{Name} {Value}";
}

public sealed class QemuDeviceEntry : System.ComponentModel.INotifyPropertyChanged
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

public sealed class QemuDevicePropDef
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DefaultValue { get; set; } = string.Empty;
    public IReadOnlyList<string> Choices { get; set; } = [];
}

public sealed class QemuInstall
{
    public bool IsAvailable { get; init; }
    public string RootDir { get; init; } = string.Empty;
    public string ImgToolPath { get; init; } = string.Empty;
    public string Version { get; init; } = svc.LocaleSvc.Current.Get("qemu.notDetected", "未检测到");
    public IReadOnlyList<QemuArch> Archs { get; init; } = [];
}

public sealed class QemuCaps
{
    public IReadOnlyList<string> Machines { get; init; } = [];
    public IReadOnlyList<string> CpuModels { get; init; } = [];
    public IReadOnlyList<string> Accelerators { get; init; } = [];
    public IReadOnlyList<string> DisplayBackends { get; init; } = [];
    public IReadOnlyList<string> VideoDevices { get; init; } = [];
    public IReadOnlyList<string> NetworkDevices { get; init; } = [];
    public IReadOnlyList<string> AudioDevices { get; init; } = [];
    public IReadOnlyList<string> AllDevices { get; init; } = [];
    public IReadOnlyList<QemuCmdOptionDef> CmdOptions { get; init; } = [];
}

public sealed record OperationResult(bool Succeeded, string Message, string Detail = "")
{
    public static OperationResult Ok(string message) => new(true, message);
    public static OperationResult Fail(string message, string detail = "") => new(false, message, detail);
}




