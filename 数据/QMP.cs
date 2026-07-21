namespace QemuWG.数据;

public sealed record QMP结果(bool Succeeded, string Output, string ErrorClass = "");

public sealed class QMP命令
{
    public string Name { get; set; } = string.Empty;
    public string ArgumentsHint { get; set; } = string.Empty;
}
