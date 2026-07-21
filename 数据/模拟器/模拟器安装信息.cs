namespace QemuWG.数据;

public sealed class QEMU安装
{
    public bool IsAvailable { get; init; }
    public string RootDir { get; init; } = string.Empty;
    public string ImgToolPath { get; init; } = string.Empty;
    public string Version { get; init; } = QemuWG.服务.语言服务.当前.获取("qemu.notDetected", "未检测到");
    public IReadOnlyList<QEMU架构> Archs { get; init; } = [];
}
