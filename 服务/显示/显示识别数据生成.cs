using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU工具服务
{
    public Task<进程结果> 生成EDID(
        QEMU安装 install,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default) =>
        进程.运行(Path.Combine(install.RootDir, "qemu-edid.exe"), arguments, cancellationToken);
}
