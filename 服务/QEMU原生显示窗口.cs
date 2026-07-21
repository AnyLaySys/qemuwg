using System.Runtime.InteropServices;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU会话
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwRestore = 9;

    public 操作结果 分离显示(虚拟机配置 vm)
    {
        Session? session;
        lock (gate) sessions.TryGetValue(vm.Id, out session);
        if (session is null || !session.IsActive)
            return 操作结果.Fail(T("session.notRunning", "虚拟机没有运行"));
        if (!支持原生窗口(session.NativeDisplayBackend))
            return 操作结果.Fail(T("display.nativeWindowUnavailable", "当前配置没有 QEMU 原生图形窗口。"));

        var window = session.NativeWindowHandle;
        if (window == 0 || !IsWindow(window))
        {
            session.Process.Refresh();
            window = session.Process.MainWindowHandle;
            session.NativeWindowHandle = window;
        }
        if (window == 0 || !IsWindow(window))
            return 操作结果.Fail(T("display.nativeWindowNotReady", "QEMU 原生图形窗口尚未就绪。"));

        ShowWindow(window, IsIconic(window) ? SwRestore : SwShow);
        SetForegroundWindow(window);
        return 操作结果.Ok(T("display.detached", "已显示 QEMU 原生图形窗口"));
    }

    private static async Task 准备原生显示窗口(Session session)
    {
        if (!支持原生窗口(session.NativeDisplayBackend)) return;
        try
        {
            for (var attempt = 0; attempt < 60 && session.IsActive; attempt++)
            {
                session.Process.Refresh();
                var window = session.Process.MainWindowHandle;
                if (window != 0 && IsWindow(window))
                {
                    session.NativeWindowHandle = window;
                    ShowWindow(window, SwHide);
                    return;
                }
                await Task.Delay(100, session.Lifetime.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            应用日志.写("Prepare native QEMU display window failed: " + exception);
        }
    }

    private static bool 支持原生窗口(string backend) =>
        backend.StartsWith("gtk", StringComparison.OrdinalIgnoreCase)
        || backend.StartsWith("sdl", StringComparison.OrdinalIgnoreCase)
        || backend.StartsWith("spice-app", StringComparison.OrdinalIgnoreCase);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);
}
