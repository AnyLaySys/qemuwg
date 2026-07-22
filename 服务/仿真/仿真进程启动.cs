using System.Diagnostics;
using System.Text;
using System.Text.Json;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU会话
{
    public 操作结果 启动(QEMU安装 install, 仿真配置 vm) => 启动(install, vm, string.Empty);

    public 操作结果 启动(QEMU安装 install, 仿真配置 vm, string loadVmTag)
    {
        lock (gate)
        {
            if (sessions.TryGetValue(vm.Id, out var existing) && !existing.Process.HasExited)
                return 操作结果.Fail(T("session.alreadyRunning", "仿真已在运行"));
        }

        var arch = install.Archs.FirstOrDefault(item => item.Id == vm.Arch);
        if (arch is null) return 操作结果.Fail(T("session.unsupportedArchitecture", "当前 QEMU 不支持该系统架构"));
        if (!File.Exists(vm.DiskPath)) return 操作结果.Fail(T("session.diskMissing", "找不到虚拟磁盘"), vm.DiskPath);
        if (string.Equals(vm.Firmware, "uefi", StringComparison.OrdinalIgnoreCase)
            && qemuSvc.查找固件(install, vm.Arch, false) is null)
            return 操作结果.Fail(T("session.uefiMissing", "找不到所选架构的 UEFI 固件，已取消启动，未回退到 BIOS。"));
        if (string.Equals(vm.Firmware, "uefi", StringComparison.OrdinalIgnoreCase))
        {
            var variablesResult = UEFI变量存储.准备(install, vm, qemuSvc.查找固件(install, vm.Arch, true));
            if (!variablesResult.Succeeded) return variablesResult;
        }
        if (vm.PhysicalStorage.Count > 0)
        {
            if (物理存储冲突检查.存在冲突(vm.PhysicalStorage))
                return 操作结果.Fail(T("session.physicalStorageConflict", "物理磁盘、分区或重叠范围不能重复挂载。"));

            var currentDevices = qemuSvc.获取物理存储().GetAwaiter().GetResult();
            foreach (var storage in vm.PhysicalStorage)
            {
                var current = currentDevices.FirstOrDefault(device =>
                    (string.IsNullOrWhiteSpace(storage.UniqueId)
                        ? device.DiskNumber == storage.DiskNumber
                        : string.Equals(device.UniqueId, storage.UniqueId, StringComparison.OrdinalIgnoreCase))
                    && string.Equals(device.Kind, storage.Kind, StringComparison.OrdinalIgnoreCase)
                    && device.PartitionNumber == storage.PartitionNumber);
                if (current is null)
                    return 操作结果.Fail(T("session.physicalStorageMissing", "找不到先前选择的物理磁盘或分区，已取消启动。"), storage.DisplayText);
                storage.DevicePath = current.DevicePath;
                storage.DiskNumber = current.DiskNumber;
                storage.Offset = current.Offset;
                storage.Size = current.Size;
            }

            lock (gate)
            {
                var inUse = sessions.Values
                    .Where(active => active.IsActive)
                    .SelectMany(active => active.PhysicalStorage)
                    .Any(activeStorage => vm.PhysicalStorage.Any(storage =>
                        物理存储冲突检查.运行时互相冲突(activeStorage, storage)));
                if (inUse)
                    return 操作结果.Fail(T("session.physicalStorageInUse", "所选物理磁盘或分区正在被其他运行中的仿真使用。"));
            }
        }

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
                         install, vm, port, displayPort, extraArguments, requiresOpenGl, loadVmTag))
                startInfo.ArgumentList.Add(argument);

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            Session? session = null;
            var startupGate = new object();
            var startupCompleted = false;
            var exitObservedDuringStartup = false;
            var logLock = new object();
            void AppendLog(string? line)
            {
                if (string.IsNullOrEmpty(line)) return;
                lock (logLock) File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
            }

            process.OutputDataReceived += (_, args) => AppendLog(args.Data);
            process.ErrorDataReceived += (_, args) => AppendLog(args.Data);
            void HandleProcessExit(Session exitedSession)
            {
                if (!exitedSession.尝试关闭()) return;
                var exitLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] QEMU exited with code {尝试获取退出码(process)}.";
                AppendLog(exitLine);
                exitedSession.Lifetime.Cancel();
                _ = 清理内嵌显示(exitedSession);
                lock (gate)
                {
                    if (sessions.TryGetValue(vm.Id, out var current) && ReferenceEquals(current, exitedSession))
                        sessions.Remove(vm.Id);
                }
                状态变化?.Invoke(this, vm);
                process.Dispose();
            }

            process.Exited += (_, _) =>
            {
                Session? exitedSession;
                lock (startupGate)
                {
                    exitObservedDuringStartup = true;
                    if (!startupCompleted) return;
                    exitedSession = session;
                }
                if (exitedSession is not null) HandleProcessExit(exitedSession);
            };

            var loggedCommand = string.Join(' ', startInfo.ArgumentList.Select(argument => JsonSerializer.Serialize(argument)));
            File.AppendAllText(
                logPath,
                $"{Environment.NewLine}[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Starting {vm.Name}{Environment.NewLine}" +
                $"Command: {JsonSerializer.Serialize(startInfo.FileName)} {loggedCommand}{Environment.NewLine}",
                Encoding.UTF8);
            if (!process.Start()) return 操作结果.Fail(T("session.startFailed", "QEMU 启动失败"));
            var createdSession = new Session(
                process,
                port,
                displayPort,
                vm.DisplayBackend,
                vm.PhysicalStorage.Select(storage => new 物理存储挂载
                {
                    DevicePath = storage.DevicePath,
                    DisplayName = storage.DisplayName,
                    Interface = storage.Interface,
                    ReadOnly = storage.ReadOnly,
                    Kind = storage.Kind,
                    DiskNumber = storage.DiskNumber,
                    PartitionNumber = storage.PartitionNumber,
                    Offset = storage.Offset,
                    Size = storage.Size,
                    UniqueId = storage.UniqueId
                }).ToList());
            bool exitedBeforeRegistration;
            lock (startupGate)
            {
                session = createdSession;
                exitedBeforeRegistration = exitObservedDuringStartup || process.HasExited;
                if (!exitedBeforeRegistration)
                {
                    lock (gate) sessions[vm.Id] = createdSession;
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    vm.IsRunning = true;
                }
                startupCompleted = true;
            }
            if (exitedBeforeRegistration)
            {
                HandleProcessExit(createdSession);
                return 操作结果.Fail(
                    T("session.exitedEarly", "QEMU 启动后立即退出"),
                    $"Exit code: {尝试获取退出码(process)}");
            }
            状态变化?.Invoke(this, vm);
            return 操作结果.Ok(T("session.started", "仿真已启动"));
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
