namespace QemuWG.数据;

public sealed record 操作结果(bool Succeeded, string Message, string Detail = "")
{
    public static 操作结果 Ok(string message) => new(true, message);
    public static 操作结果 Fail(string message, string detail = "") => new(false, message, detail);
}
