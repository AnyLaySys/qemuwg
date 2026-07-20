namespace QemuWG.data;

public sealed record QmpResult(bool Succeeded, string Output, string ErrorClass = "");

public sealed class QmpCmdInfo
{
    public string Name { get; set; } = string.Empty;
    public string ArgumentsHint { get; set; } = string.Empty;
}


