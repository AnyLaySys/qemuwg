namespace QemuWG.data;

public sealed record GuestAgentResult(bool Succeeded, string Output, string ErrorClass = "");

public sealed class GuestAgentCmdInfo
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool SuccessResponse { get; set; } = true;
}


