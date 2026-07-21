using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU镜像
{
    public Task<进程结果> 执行(
        QEMU安装 install,
        磁盘命令 command,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        var built = 构建参数(command, values);
        var arguments = built.GlobalArgs.Concat([command.Name]).Concat(built.CmdArgs);
        return 进程.运行(install.ImgToolPath, arguments, cancellationToken);
    }
}
