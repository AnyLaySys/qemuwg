namespace QemuWG.服务;

internal sealed class QMP显示连接异常(string 命令, string 错误类别, string 详细信息)
    : InvalidOperationException($"QMP {命令} 失败：{详细信息}")
{
    public string 错误类别 { get; } = 错误类别;
}
