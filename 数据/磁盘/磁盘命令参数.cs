namespace QemuWG.数据;

public sealed record 磁盘命令参数(
    IReadOnlyList<string> GlobalArgs,
    IReadOnlyList<string> CmdArgs,
    string Preview);
