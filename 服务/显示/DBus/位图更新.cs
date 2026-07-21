namespace QemuWG.服务;

public sealed record 位图更新(
    int 横向偏移,
    int 纵向偏移,
    int 宽度,
    int 高度,
    uint 跨距,
    uint 像素格式,
    byte[] 数据);
