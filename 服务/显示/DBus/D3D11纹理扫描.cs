namespace QemuWG.服务;

public sealed record D3D11纹理扫描(
    nint 共享句柄,
    uint 纹理宽度,
    uint 纹理高度,
    bool 原点在顶部,
    uint 横向偏移,
    uint 纵向偏移,
    uint 显示宽度,
    uint 显示高度);
