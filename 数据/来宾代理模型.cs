namespace QemuWG.数据;

public sealed record 来宾代理结果(bool Succeeded, string Output, string ErrorClass = "");

public sealed class 来宾代理命令
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool SuccessResponse { get; set; } = true;
}
