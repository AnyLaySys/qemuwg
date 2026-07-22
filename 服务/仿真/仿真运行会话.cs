using System.Diagnostics;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU会话
{
    private static string T(string key, string fallback) => 语言服务.当前.获取(key, fallback);

    private readonly Dictionary<string, Session> sessions = [];
    private readonly object gate = new();
    private readonly QEMU服务 qemuSvc;

    public QEMU会话(QEMU服务 qemuSvc)
    {
        this.qemuSvc = qemuSvc;
    }

    public event EventHandler<仿真配置>? 状态变化;

    public bool 存在QMP会话(仿真配置 vm)
    {
        lock (gate)
            return sessions.TryGetValue(vm.Id, out var session) && session.IsActive;
    }

    public bool 尝试获取显示端口(仿真配置 vm, out int displayPort)
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

    private sealed class Session(
        Process process,
        int qmpPort,
        int displayPort,
        string nativeDisplayBackend,
        IReadOnlyList<物理存储挂载> physicalStorage)
    {
        private int closed;

        public Process Process { get; } = process;
        public int QmpPort { get; } = qmpPort;
        public int DisplayPort { get; } = displayPort;
        public string NativeDisplayBackend { get; } = nativeDisplayBackend;
        public IReadOnlyList<物理存储挂载> PhysicalStorage { get; } = physicalStorage;
        public nint NativeWindowHandle { get; set; }
        public SemaphoreSlim DisplayConnectionGate { get; } = new(1, 1);
        public long DisplayRequestVersion;
        public DBus显示传输? DisplayTransport { get; set; }
        public bool DisplayListenerRegistered { get; set; }
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

        public bool 尝试关闭() => Interlocked.Exchange(ref closed, 1) == 0;
    }
}
