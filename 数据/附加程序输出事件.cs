namespace QemuWG.数据;

public sealed record 工具输出事件(string Scope, string Tool, string Text, bool IsError = false);
