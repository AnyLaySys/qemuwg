using System.Diagnostics;
using System.Text;
using System.Text.Json;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU会话
{
    public 操作结果 启动(QEMU安装 install, 虚拟机配置 vm)
    {
        lock (gate)
        {
            if (sessions.TryGetValue(vm.Id, out var existing) && !existing.Process.HasExited)
                return 操作结果.Fail(T("session.alreadyRunning", "虚拟机已在运行"));
        }

        var arch = install.Archs.FirstOrDefault(item => item.Id == vm.Arch);
        if (arch is null) return 操作结果.Fail(T("session.unsupportedArchitecture", "当前 QEMU 不支持该系统架构"));
        if (!File.Exists(vm.DiskPath)) return 操作结果.Fail(T("session.diskMissing", "找不到虚拟磁盘"), vm.DiskPath);

        try
        {
            IReadOnlyList<string> extraArguments = 解析命令行(vm.ExtraArgs);
            var requiresOpenGl = QEMU显示要求.需要OpenGL(vm, extraArguments);
            if (requiresOpenGl)
            {
                foreach (var configuredDisplay in QEMU显示要求.枚举显示后端(vm, extraArguments))
                {
                    if (QEMU显示要求.显式关闭OpenGL(configuredDisplay))
                        return 操作结果.Fail(
                            T("display.openGlDisabled", "所选显卡需要 OpenGL，但显示后端显式配置了 gl=off。"),
                            configuredDisplay);
                    if (!QEMU显示要求.支持OpenGL(configuredDisplay))
                        return 操作结果.Fail(
                            T("display.openGlUnsupported", "所选显卡需要 OpenGL，但当前显示后端不支持 OpenGL。"),
                            configuredDisplay);
                }
                extraArguments = QEMU显示要求.启用额外显示OpenGL(extraArguments);
            }

            var displayPort = 查找空闲VNC端口();
            int port;
            do port = 查找空闲端口(); while (port == displayPort);
            int guestAgentPort;
            do guestAgentPort = 查找空闲端口(); while (guestAgentPort == port || guestAgentPort == displayPort);
            var logPath = Path.Combine(vm.DirPath, "qemu.log");
            var startInfo = new ProcessStartInfo(arch.ExecutablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = vm.DirPath
            };
            foreach (var argument in BuildArgs(
                         install, vm, port, guestAgentPort, displayPort, extraArguments, requiresOpenGl))
                startInfo.ArgumentList.Add(argument);

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            Session? session = null;
            var logLock = new object();
            void AppendLog(string? line)
            {
                if (string.IsNullOrEmpty(line)) return;
                lock (logLock) File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
            }

            process.OutputDataReceived += (_, args) => AppendLog(args.Data);
            process.ErrorDataReceived += (_, args) => AppendLog(args.Data);
            process.Exited += (_, _) =>
            {
                if (session is null || !session.尝试关闭()) return;
                var exitLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] QEMU exited with code {尝试获取退出码(process)}.";
                AppendLog(exitLine);
                session.Lifetime.Cancel();
                _ = 清理内嵌显示(session);
                lock (gate)
                {
                    if (sessions.TryGetValue(vm.Id, out var current) && ReferenceEquals(current, session))
                        sessions.Remove(vm.Id);
                }
                状态变化?.Invoke(this, vm);
                process.Dispose();
            };

            var loggedCommand = string.Join(' ', startInfo.ArgumentList.Select(argument => JsonSerializer.Serialize(argument)));
            File.AppendAllText(
                logPath,
                $"{Environment.NewLine}[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Starting {vm.Name}{Environment.NewLine}" +
                $"Command: {JsonSerializer.Serialize(startInfo.FileName)} {loggedCommand}{Environment.NewLine}",
                Encoding.UTF8);
            if (!process.Start()) return 操作结果.Fail(T("session.startFailed", "QEMU 启动失败"));
            session = new Session(process, port, guestAgentPort, displayPort, vm.DisplayBackend);
            lock (gate) sessions[vm.Id] = session;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            vm.IsRunning = true;
            状态变化?.Invoke(this, vm);
            return 操作结果.Ok(T("session.started", "虚拟机已启动"));
        }
        catch (Exception exception)
        {
            return 操作结果.Fail(T("session.startFailed", "QEMU 启动失败"), exception.Message);
        }
    }

    private static int 尝试获取退出码(Process process)
    {
        try { return process.ExitCode; }
        catch { return -1; }
    }
}
