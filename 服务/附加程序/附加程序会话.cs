using System.Diagnostics;
using System.Text;
using QemuWG.数据;

namespace QemuWG.服务;

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
