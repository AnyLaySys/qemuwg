using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU工具服务
{
    public Task<进程结果> 运行工具(
        QEMU安装 install,
        string tool,
        string rawArguments,
        CancellationToken cancellationToken = default) =>
        进程.运行(
            Path.Combine(install.RootDir, tool),
            命令行.分割(rawArguments),
            cancellationToken);
}
