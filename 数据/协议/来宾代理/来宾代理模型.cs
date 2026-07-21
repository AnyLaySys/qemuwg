namespace QemuWG.数据;

public sealed record 来宾代理结果(bool Succeeded, string Output, string ErrorClass = "");
