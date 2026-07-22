namespace QemuWG.数据;

public sealed class 物理存储挂载
{
    public string DevicePath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Interface { get; set; } = "virtio";
    public bool ReadOnly { get; set; } = true;
    public string Kind { get; set; } = "disk";
    public int DiskNumber { get; set; }
    public int PartitionNumber { get; set; }
    public long Offset { get; set; }
    public long Size { get; set; }
    public string UniqueId { get; set; } = string.Empty;

    public string DisplayText => string.IsNullOrWhiteSpace(DisplayName) ? DevicePath : DisplayName;
    public string AccessModeText => ReadOnly
        ? QemuWG.服务.语言服务.当前.获取("vmEditor.readOnly", "只读")
        : QemuWG.服务.语言服务.当前.获取("vmEditor.readWrite", "读写");
}
