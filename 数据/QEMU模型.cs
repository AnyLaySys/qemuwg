namespace QemuWG.数据;

public sealed record QEMU架构(string Id, string DisplayName, string ExecutablePath);

public sealed class QEMU命令选项
{
    public string Name { get; set; } = string.Empty;
    public string Syntax { get; set; } = string.Empty;
}

public sealed class QEMU选项
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ArgumentName => Name.StartsWith('-') ? Name : $"-{Name}";
    public string DisplayText => string.IsNullOrWhiteSpace(Value) ? ArgumentName : $"{ArgumentName} {Value}";
}

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

public sealed class QEMU设备属性
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DefaultValue { get; set; } = string.Empty;
    public IReadOnlyList<string> Choices { get; set; } = [];
}

public sealed class QEMU安装
{
    public bool IsAvailable { get; init; }
    public string RootDir { get; init; } = string.Empty;
    public string ImgToolPath { get; init; } = string.Empty;
    public string Version { get; init; } = QemuWG.服务.语言服务.当前.获取("qemu.notDetected", "未检测到");
    public IReadOnlyList<QEMU架构> Archs { get; init; } = [];
}

public sealed class QEMU能力
{
    public IReadOnlyList<string> Machines { get; init; } = [];
    public IReadOnlyList<string> CpuModels { get; init; } = [];
    public IReadOnlyList<string> Accelerators { get; init; } = [];
    public IReadOnlyList<string> DisplayBackends { get; init; } = [];
    public IReadOnlyList<string> VideoDevices { get; init; } = [];
    public IReadOnlyList<string> NetworkBackends { get; init; } = [];
    public IReadOnlyList<string> NetworkDevices { get; init; } = [];
    public IReadOnlyList<string> AudioBackends { get; init; } = [];
    public IReadOnlyList<string> AudioDevices { get; init; } = [];
    public IReadOnlyList<string> AllDevices { get; init; } = [];
    public IReadOnlyList<QEMU命令选项> CmdOptions { get; init; } = [];
}

public sealed record 操作结果(bool Succeeded, string Message, string Detail = "")
{
    public static 操作结果 Ok(string message) => new(true, message);
    public static 操作结果 Fail(string message, string detail = "") => new(false, message, detail);
}
