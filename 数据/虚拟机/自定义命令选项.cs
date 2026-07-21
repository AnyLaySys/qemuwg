namespace QemuWG.数据;

public sealed class QEMU选项
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ArgumentName => Name.StartsWith('-') ? Name : $"-{Name}";
    public string DisplayText => string.IsNullOrWhiteSpace(Value) ? ArgumentName : $"{ArgumentName} {Value}";
}
