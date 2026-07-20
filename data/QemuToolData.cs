namespace QemuWG.data;

public sealed class QemuIoCmdInfo
{
    public string Name { get; set; } = string.Empty;
    public string Syntax { get; set; } = string.Empty;
}

public sealed record ToolOutputEvent(string Tool, string Text, bool IsError = false);


