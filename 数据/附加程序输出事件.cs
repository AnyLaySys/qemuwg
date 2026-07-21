namespace QemuWG.数据;

public sealed record 工具输出事件(string Tool, string Text, bool IsError = false);
