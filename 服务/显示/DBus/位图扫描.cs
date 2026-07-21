namespace QemuWG.服务;

public sealed record 位图扫描(uint 宽度, uint 高度, uint 跨距, uint 像素格式, byte[] 数据);
