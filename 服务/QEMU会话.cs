using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU会话
{
    private static readonly HashSet<string> GuestAgentArchs = new(StringComparer.OrdinalIgnoreCase)
    {
        "aarch64", "alpha", "arm", "hppa", "i386", "loongarch64", "m68k", "mips", "mips64",
        "mips64el", "mipsel", "or1k", "ppc", "ppc64", "riscv32", "riscv64", "s390x", "sh4", "x86_64"
    };
    private static string T(string key, string fallback) => 语言服务.Current.Get(key, fallback);

    private readonly Dictionary<string, Session> sessions = [];
    private readonly object gate = new();
    private readonly QEMU服务 qemuSvc;

    public QEMU会话(QEMU服务 qemuSvc)
    {
        this.qemuSvc = qemuSvc;
    }

    public event EventHandler<虚拟机配置>? StateChanged;

    public 操作结果 Start(QEMU安装 install, 虚拟机配置 vm)
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
            var displayPort = FindFreeVncPort();
            int port;
            do port = FindFreePort(); while (port == displayPort);
            int guestAgentPort;
            do guestAgentPort = FindFreePort(); while (guestAgentPort == port || guestAgentPort == displayPort);
            var logPath = Path.Combine(vm.DirPath, "qemu.log");
            var startInfo = new ProcessStartInfo(arch.ExecutablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = vm.DirPath
            };
            foreach (var argument in BuildArgs(install, vm, port, guestAgentPort, displayPort)) startInfo.ArgumentList.Add(argument);

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
                if (session is null || !session.TryClose()) return;
                var exitLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] QEMU exited with code {TryGetExitCode(process)}.";
                AppendLog(exitLine);
                session.Lifetime.Cancel();
                lock (gate)
                {
                    if (sessions.TryGetValue(vm.Id, out var current) && ReferenceEquals(current, session))
                        sessions.Remove(vm.Id);
                }
                StateChanged?.Invoke(this, vm);
                process.Dispose();
            };

            File.AppendAllText(logPath, $"{Environment.NewLine}[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Starting {vm.Name}{Environment.NewLine}", Encoding.UTF8);
            if (!process.Start()) return 操作结果.Fail(T("session.startFailed", "QEMU 启动失败"));
            session = new Session(process, port, guestAgentPort, displayPort);
            lock (gate) sessions[vm.Id] = session;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            vm.IsRunning = true;
            StateChanged?.Invoke(this, vm);
            return 操作结果.Ok(T("session.started", "虚拟机已启动"));
        }
        catch (Exception exception)
        {
            return 操作结果.Fail(T("session.startFailed", "QEMU 启动失败"), exception.Message);
        }
    }

    public async Task<操作结果> ShutdownAsync(虚拟机配置 vm)
    {
        var result = await ExecuteQmpAsync(vm, "system_powerdown");
        return result.Succeeded
            ? 操作结果.Ok(T("session.shutdownRequested", "已发送关机请求"))
            : 操作结果.Fail(T("session.shutdownFailed", "发送关机请求失败"), result.Output);
    }

    public async Task<bool> WaitForExitAsync(虚拟机配置 vm, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Session? session;
        lock (gate) sessions.TryGetValue(vm.Id, out session);
        if (session is null || !session.IsActive) return true;

        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCancellation.Token, session.Lifetime.Token);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, linkedCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        return !session.IsActive;
    }

    public bool HasQmpSession(虚拟机配置 vm)
    {
        lock (gate)
            return sessions.TryGetValue(vm.Id, out var session) && session.IsActive;
    }

    public bool TryGetDisplayPort(虚拟机配置 vm, out int displayPort)
    {
        lock (gate)
        {
            if (sessions.TryGetValue(vm.Id, out var session) && session.IsActive)
            {
                displayPort = session.DisplayPort;
                return true;
            }
        }
        displayPort = 0;
        return false;
    }

    public 操作结果 ForceStop(虚拟机配置 vm)
    {
        Session? session;
        lock (gate) sessions.TryGetValue(vm.Id, out session);
        if (session is null || !session.IsActive) return 操作结果.Fail(T("session.notRunning", "虚拟机没有运行"));
        try
        {
            session.Process.Kill(true);
            return 操作结果.Ok(T("session.forceStopped", "虚拟机已强制停止"));
        }
        catch (Exception exception)
        {
            return 操作结果.Fail(T("session.stopFailed", "无法停止虚拟机"), exception.Message);
        }
    }

    private IEnumerable<string> BuildArgs(
        QEMU安装 install,
        虚拟机配置 vm,
        int qmpPort,
        int guestAgentPort,
        int displayPort)
    {
        var arguments = new List<string> { "-name", vm.Name, "-m", vm.MemoryMb.ToString(), "-smp", vm.CpuCount.ToString() };
        if (!string.IsNullOrWhiteSpace(vm.MachineType)) arguments.AddRange(["-machine", vm.MachineType]);
        if (!string.IsNullOrWhiteSpace(vm.Accelerator) && vm.Accelerator != "auto") arguments.AddRange(["-accel", vm.Accelerator]);
        if (!string.IsNullOrWhiteSpace(vm.CpuModel) && vm.CpuModel != "default") arguments.AddRange(["-cpu", vm.CpuModel]);

        if (vm.Firmware == "uefi")
        {
            var code = qemuSvc.FindFirmware(install, vm.Arch, false);
            var variablesTemplate = qemuSvc.FindFirmware(install, vm.Arch, true);
            if (code is not null)
            {
                arguments.AddRange(["-drive", $"if=pflash,format=raw,readonly=on,file={code}"]);
                if (variablesTemplate is not null)
                {
                    var variables = Path.Combine(vm.DirPath, "uefi-vars.fd");
                    if (!File.Exists(variables)) File.Copy(variablesTemplate, variables);
                    arguments.AddRange(["-drive", $"if=pflash,format=raw,file={variables}"]);
                }
            }
        }

        arguments.AddRange(["-drive", $"file={vm.DiskPath},format=qcow2,if=virtio,id=system-disk"]);
        if (File.Exists(vm.IsoPath)) arguments.AddRange(["-drive", $"file={vm.IsoPath},media=cdrom,readonly=on,id=install-media"]);
        if (!string.IsNullOrWhiteSpace(vm.BootOrder)) arguments.AddRange(["-boot", $"order={vm.BootOrder}"]);
        if (!string.IsNullOrWhiteSpace(vm.RtcBase)) arguments.AddRange(["-rtc", $"base={vm.RtcBase}"]);

        arguments.AddRange([
            "-display", "none",
            "-vnc", $"127.0.0.1:{displayPort - 5900},share=force-shared"
        ]);
        if (!string.IsNullOrWhiteSpace(vm.VideoDevice) && vm.VideoDevice != "auto") arguments.AddRange(["-device", vm.VideoDevice]);
        if (vm.NetworkMode != "none")
        {
            if (vm.NetworkModel == "auto") arguments.AddRange(["-nic", "user"]);
            else arguments.AddRange(["-nic", $"user,model={vm.NetworkModel}"]);
        }
        if (vm.AudioBackend == "none") arguments.AddRange(["-audiodev", "driver=none,id=audio0"]);
        else if (!string.IsNullOrWhiteSpace(vm.AudioBackend)) arguments.AddRange(["-audiodev", $"driver={vm.AudioBackend},id=audio0"]);
        if (!string.IsNullOrWhiteSpace(vm.AudioDevice) && vm.AudioDevice != "auto")
            arguments.AddRange(["-device", vm.AudioDevice]);

        foreach (var device in vm.Devices.Where(device => !string.IsNullOrWhiteSpace(device.Model)))
        {
            var value = device.Model.Trim();
            if (device.Properties.Count > 0)
                value += "," + string.Join(',', device.Properties
                    .Where(item => !string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
                    .Select(item => $"{item.Key.Trim()}={item.Value.Trim()}"));
            arguments.AddRange(["-device", value]);
        }

        arguments.AddRange(["-qmp", $"tcp:127.0.0.1:{qmpPort},server=on,wait=off"]);
        if (vm.EnableGuestAgent && GuestAgentArchs.Contains(vm.Arch))
        {
            arguments.AddRange([
                "-device", "virtio-serial",
                "-chardev", $"socket,id=qga0,host=127.0.0.1,port={guestAgentPort},server=on,wait=off",
                "-device", "virtserialport,chardev=qga0,name=org.qemu.guest_agent.0"
            ]);
        }
        foreach (var option in vm.QemuOpts)
        {
            var name = option.Name.Trim().TrimStart('-');
            if (name.Length == 0) continue;
            arguments.Add("-" + name);
            if (!string.IsNullOrWhiteSpace(option.Value)) arguments.Add(option.Value.Trim());
        }
        arguments.AddRange(ParseCmdLine(vm.ExtraArgs));
        return arguments;
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static int FindFreeVncPort()
    {
        for (var port = 5900; port <= 5999; port++)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return port;
            }
            catch (SocketException)
            {
            }
        }
        throw new IOException(T("session.displayPortUnavailable", "没有可用的本机显示端口。"));
    }

    private static IReadOnlyList<string> ParseCmdLine(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return [];
        var pointer = CommandLineToArgvW(commandLine, out var count);
        if (pointer == IntPtr.Zero) return [];
        try
        {
            var result = new string[count];
            for (var index = 0; index < count; index++)
            {
                var item = Marshal.ReadIntPtr(pointer, index * IntPtr.Size);
                result[index] = Marshal.PtrToStringUni(item) ?? string.Empty;
            }
            return result;
        }
        finally
        {
            LocalFree(pointer);
        }
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string commandLine, out int argumentCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static int TryGetExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch { return -1; }
    }

    private sealed class Session(Process process, int qmpPort, int guestAgentPort, int displayPort)
    {
        private int closed;

        public Process Process { get; } = process;
        public int QmpPort { get; } = qmpPort;
        public int GuestAgentPort { get; } = guestAgentPort;
        public int DisplayPort { get; } = displayPort;
        public CancellationTokenSource Lifetime { get; } = new();
        public bool IsActive
        {
            get
            {
                if (Volatile.Read(ref closed) != 0) return false;
                try { return !Process.HasExited; }
                catch { return false; }
            }
        }

        public bool TryClose() => Interlocked.Exchange(ref closed, 1) == 0;
    }
}
