using System.Diagnostics;
using System.Text;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed class QEMU工具会话
{
    private readonly Dictionary<(string Scope, string Tool), Process> processes = new(new ScopeToolComparer());
    private readonly object gate = new();

    public event EventHandler<工具输出事件>? 收到输出;

    public 操作结果 启动(QEMU安装 install, string scope, string tool, string rawArguments)
    {
        var key = (规范作用域(scope), tool);
        try
        {
            lock (gate)
            {
                if (processes.TryGetValue(key, out var existing) && !existing.HasExited)
                    return 操作结果.Fail(语言服务.当前.获取("tools.alreadyRunning", "工具已在运行"));

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

                var process = new Process { StartInfo = startInfo };
                process.OutputDataReceived += (_, args) => 发送输出(key, process, args.Data, false);
                process.ErrorDataReceived += (_, args) => 发送输出(key, process, args.Data, true);
                process.Exited += (_, _) => 处理退出(key, process);
                if (!process.Start())
                {
                    process.Dispose();
                    return 操作结果.Fail(语言服务.当前.获取("process.startFailed", "无法启动进程"));
                }

                processes[key] = process;
                process.EnableRaisingEvents = true;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                return 操作结果.Ok(语言服务.当前.获取("tools.started", "工具已启动"));
            }
        }
        catch (Exception exception)
        {
            return 操作结果.Fail(语言服务.当前.获取("tools.startFailed", "工具启动失败"), exception.Message);
        }
    }

    public 操作结果 停止(string scope, string tool)
    {
        Process? process;
        var key = (规范作用域(scope), tool);
        lock (gate) processes.TryGetValue(key, out process);
        if (process is null)
            return 操作结果.Fail(语言服务.当前.获取("tools.notRunning", "工具没有运行"));
        try
        {
            if (process.HasExited)
                return 操作结果.Fail(语言服务.当前.获取("tools.notRunning", "工具没有运行"));
            process.Kill(true);
            return 操作结果.Ok(语言服务.当前.获取("tools.stopped", "工具已停止"));
        }
        catch (Exception exception)
        {
            return 操作结果.Fail(语言服务.当前.获取("tools.stopFailed", "无法停止工具"), exception.Message);
        }
    }

    public bool 正在运行(string scope, string tool)
    {
        lock (gate) return processes.TryGetValue((规范作用域(scope), tool), out var process) && !process.HasExited;
    }

    public void 停止全部()
    {
        Process[] running;
        lock (gate)
        {
            running = processes.Values.Distinct().ToArray();
            processes.Clear();
        }

        停止进程(running);
    }

    public void 停止作用域(string scope)
    {
        var normalizedScope = 规范作用域(scope);
        Process[] running;
        lock (gate)
        {
            var keys = processes.Keys
                .Where(key => string.Equals(key.Scope, normalizedScope, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            running = keys.Select(key => processes[key]).Distinct().ToArray();
            foreach (var key in keys) processes.Remove(key);
        }

        停止进程(running);
    }

    private static void 停止进程(IEnumerable<Process> running)
    {
        foreach (var process in running)
        {
            try
            {
                if (!process.HasExited) process.Kill(true);
            }
            catch
            {
                // 应用退出期间尽最大努力清理所有附加进程。
            }
        }
    }

    private void 处理退出((string Scope, string Tool) key, Process process)
    {
        int exitCode;
        try { exitCode = process.ExitCode; }
        catch { exitCode = -1; }

        var current = false;
        lock (gate)
        {
            if (processes.TryGetValue(key, out var registered) && ReferenceEquals(registered, process))
            {
                processes.Remove(key);
                current = true;
            }
        }

        if (current)
            收到输出?.Invoke(this, new 工具输出事件(key.Scope, key.Tool, $"exit {exitCode}", exitCode != 0));
        process.Dispose();
    }

    private void 发送输出((string Scope, string Tool) key, Process process, string? text, bool error)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (gate)
        {
            if (!processes.TryGetValue(key, out var registered) || !ReferenceEquals(registered, process)) return;
        }
        收到输出?.Invoke(this, new 工具输出事件(key.Scope, key.Tool, text, error));
    }

    private static string 规范作用域(string scope) => string.IsNullOrWhiteSpace(scope) ? "global" : scope.Trim();

    private sealed class ScopeToolComparer : IEqualityComparer<(string Scope, string Tool)>
    {
        public bool Equals((string Scope, string Tool) left, (string Scope, string Tool) right) =>
            string.Equals(left.Scope, right.Scope, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Tool, right.Tool, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Scope, string Tool) value) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Scope),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Tool));
    }
}
