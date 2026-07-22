namespace QemuWG.数据;

public sealed class 物理存储设备
{
    public string DevicePath { get; init; } = string.Empty;
    public string FriendlyName { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public int DiskNumber { get; init; }
    public int PartitionNumber { get; init; }
    public long Size { get; init; }
    public string PartitionType { get; init; } = string.Empty;

    public long Offset { get; init; }
    public string UniqueId { get; init; } = string.Empty;
    public bool IsBoot { get; init; }
    public bool IsSystem { get; init; }

    public string DisplayName => Kind == "partition"
        ? $"{QemuWG.服务.语言服务.当前.获取("device.disk", "磁盘")} {DiskNumber} · {QemuWG.服务.语言服务.当前.获取("vmEditor.partition", "分区")} {PartitionNumber} · {格式化容量(Size)} · {PartitionType}"
        : $"{QemuWG.服务.语言服务.当前.获取("device.disk", "磁盘")} {DiskNumber} · {FriendlyName} · {格式化容量(Size)}";

    private static string 格式化容量(long bytes)
    {
        if (bytes >= 1L << 40) return $"{bytes / (double)(1L << 40):0.##} TB";
        if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):0.##} GB";
        if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):0.##} MB";
        return $"{bytes} B";
    }
}
