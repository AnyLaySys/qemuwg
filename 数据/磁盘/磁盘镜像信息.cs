namespace QemuWG.数据;

public sealed class 磁盘镜像信息
{
    public string Format { get; init; } = "unknown";
    public long VirtualSize { get; init; }
    public long ActualSize { get; init; }
    public string BackingFile { get; init; } = string.Empty;
}
