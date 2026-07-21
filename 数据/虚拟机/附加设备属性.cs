namespace QemuWG.数据;

public sealed class QEMU设备属性
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DefaultValue { get; set; } = string.Empty;
    public IReadOnlyList<string> Choices { get; set; } = [];
}
