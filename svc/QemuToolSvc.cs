using System.Diagnostics;
using System.Text;
using QemuWG.data;

namespace QemuWG.svc;

public sealed class QemuToolSvc
{
    public async Task<IReadOnlyList<QemuIoCmdInfo>> GetQemuIoCommandsAsync(QemuInstall install)
    {
        var path = Path.Combine(install.RootDir, "qemu-io.exe");
        var result = await ProcessRunner.RunAsync(path, ["-c", "help"]);
        return result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                var separator = line.IndexOf(" -- ", StringComparison.Ordinal);
                var syntax = separator >= 0 ? line[..separator].Trim() : line.Trim();
                var name = syntax.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                return new QemuIoCmdInfo { Name = name, Syntax = syntax };
            })
            .Where(command => command.Name.Length > 0 && command.Name != "Use")
            .ToList();
    }

    public Task<ProcessResult> RunQemuIoAsync(
        QemuInstall install,
        string image,
        string command,
        IEnumerable<string> options,
        CancellationToken cancellationToken = default)
    {
        var arguments = options.Concat(["-c", command, image]);
        return ProcessRunner.RunAsync(Path.Combine(install.RootDir, "qemu-io.exe"), arguments, cancellationToken);
    }

    public Task<ProcessResult> GenerateEdidAsync(
        QemuInstall install,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default) =>
        ProcessRunner.RunAsync(Path.Combine(install.RootDir, "qemu-edid.exe"), arguments, cancellationToken);

    public Task<ProcessResult> RunToolAsync(
        QemuInstall install,
        string tool,
        string rawArguments,
        CancellationToken cancellationToken = default) =>
        ProcessRunner.RunAsync(
            Path.Combine(install.RootDir, tool),
            CmdLineParser.Split(rawArguments),
            cancellationToken);
}

public sealed class QemuToolSessionMgr
{
    private readonly Dictionary<string, Process> processes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();

    public event EventHandler<ToolOutputEvent>? OutputReceived;

    public OperationResult Start(QemuInstall install, string tool, string rawArguments)
    {
        lock (gate)
        {
            if (processes.TryGetValue(tool, out var existing) && !existing.HasExited)
                return OperationResult.Fail(LocaleSvc.Current.Get("tools.alreadyRunning", "工具已在运行"));
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
            foreach (var argument in CmdLineParser.Split(rawArguments)) startInfo.ArgumentList.Add(argument);

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, args) => Emit(tool, args.Data, false);
            process.ErrorDataReceived += (_, args) => Emit(tool, args.Data, true);
            process.Exited += (_, _) =>
            {
                Emit(tool, $"exit {process.ExitCode}", process.ExitCode != 0);
                lock (gate) processes.Remove(tool);
                process.Dispose();
            };
            if (!process.Start()) return OperationResult.Fail(LocaleSvc.Current.Get("process.startFailed", "无法启动进程"));
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            lock (gate) processes[tool] = process;
            return OperationResult.Ok(LocaleSvc.Current.Get("tools.started", "工具已启动"));
        }
        catch (Exception exception)
        {
            return OperationResult.Fail(LocaleSvc.Current.Get("tools.startFailed", "工具启动失败"), exception.Message);
        }
    }

    public OperationResult Stop(string tool)
    {
        Process? process;
        lock (gate) processes.TryGetValue(tool, out process);
        if (process is null || process.HasExited)
            return OperationResult.Fail(LocaleSvc.Current.Get("tools.notRunning", "工具没有运行"));
        try
        {
            process.Kill(true);
            return OperationResult.Ok(LocaleSvc.Current.Get("tools.stopped", "工具已停止"));
        }
        catch (Exception exception)
        {
            return OperationResult.Fail(LocaleSvc.Current.Get("tools.stopFailed", "无法停止工具"), exception.Message);
        }
    }

    public bool IsRunning(string tool)
    {
        lock (gate) return processes.TryGetValue(tool, out var process) && !process.HasExited;
    }

    private void Emit(string tool, string? text, bool error)
    {
        if (!string.IsNullOrWhiteSpace(text)) OutputReceived?.Invoke(this, new ToolOutputEvent(tool, text, error));
    }
}




