namespace QemuWG.数据;

public sealed class QEMUIO命令
{
    public string Name { get; set; } = string.Empty;
    public string Syntax { get; set; } = string.Empty;
}

public sealed record 工具输出事件(string Tool, string Text, bool IsError = false);
