namespace QemuWG.服务;

public sealed record 共享映射扫描(
    nint 共享句柄,
    uint 映射偏移,
    uint 宽度,
    uint 高度,
    uint 跨距,
    uint 像素格式);
