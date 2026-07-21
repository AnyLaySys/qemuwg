using System.Diagnostics;
using System.Text;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed class QEMU工具服务
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

    public Task<进程结果> 生成EDID(
        QEMU安装 install,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default) =>
        进程.运行(Path.Combine(install.RootDir, "qemu-edid.exe"), arguments, cancellationToken);

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

public sealed class QEMU工具会话
{
    private readonly Dictionary<string, Process> processes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();

    public event EventHandler<工具输出事件>? 收到输出;

    public 操作结果 启动(QEMU安装 install, string tool, string rawArguments)
    {
        lock (gate)
        {
            if (processes.TryGetValue(tool, out var existing) && !existing.HasExited)
                return 操作结果.Fail(语言服务.当前.获取("tools.alreadyRunning", "工具已在运行"));
        }

        try
        {
            var startInfo = new ProcessStartInfo(Path.Combine(install.RootDir, tool))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var argument in 命令行.分割(rawArguments)) startInfo.ArgumentList.Add(argument);

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, args) => 发送输出(tool, args.Data, false);
            process.ErrorDataReceived += (_, args) => 发送输出(tool, args.Data, true);
            process.Exited += (_, _) =>
            {
                发送输出(tool, $"exit {process.ExitCode}", process.ExitCode != 0);
                lock (gate) processes.Remove(tool);
                process.Dispose();
            };
            if (!process.Start()) return 操作结果.Fail(语言服务.当前.获取("process.startFailed", "无法启动进程"));
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            lock (gate) processes[tool] = process;
            return 操作结果.Ok(语言服务.当前.获取("tools.started", "工具已启动"));
        }
        catch (Exception exception)
        {
            return 操作结果.Fail(语言服务.当前.获取("tools.startFailed", "工具启动失败"), exception.Message);
        }
    }

    public 操作结果 停止(string tool)
    {
        Process? process;
        lock (gate) processes.TryGetValue(tool, out process);
        if (process is null || process.HasExited)
            return 操作结果.Fail(语言服务.当前.获取("tools.notRunning", "工具没有运行"));
        try
        {
            process.Kill(true);
            return 操作结果.Ok(语言服务.当前.获取("tools.stopped", "工具已停止"));
        }
        catch (Exception exception)
        {
            return 操作结果.Fail(语言服务.当前.获取("tools.stopFailed", "无法停止工具"), exception.Message);
        }
    }

    public bool 正在运行(string tool)
    {
        lock (gate) return processes.TryGetValue(tool, out var process) && !process.HasExited;
    }

    private void 发送输出(string tool, string? text, bool error)
    {
        if (!string.IsNullOrWhiteSpace(text)) 收到输出?.Invoke(this, new 工具输出事件(tool, text, error));
    }
}
