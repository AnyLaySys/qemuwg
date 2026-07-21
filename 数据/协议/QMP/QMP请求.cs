namespace QemuWG.数据;

public sealed record QMP请求(string 命令, string 参数 = "");
