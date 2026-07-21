using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU工具服务
{
    public async Task<IReadOnlyList<QEMUIO命令>> 获取QEMU输入输出命令(QEMU安装 install)
    {
        var path = Path.Combine(install.RootDir, "qemu-io.exe");
        var result = await 进程.运行(path, ["-c", "help"]);
        return result.输出.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var separator = line.IndexOf(" -- ", StringComparison.Ordinal);
                var syntax = separator >= 0 ? line[..separator].Trim() : line.Trim();
                var name = syntax.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                return new QEMUIO命令 { Name = name, Syntax = syntax };
            })
            .Where(command => command.Name.Length > 0 && command.Name != "Use")
            .ToList();
    }

    public Task<进程结果> 运行QEMU输入输出(
        QEMU安装 install,
        string image,
        string command,
        IEnumerable<string> options,
        CancellationToken cancellationToken = default)
    {
        var arguments = options.Concat(["-c", command, image]);
        return 进程.运行(Path.Combine(install.RootDir, "qemu-io.exe"), arguments, cancellationToken);
    }
}
