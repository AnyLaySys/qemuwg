using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU会话
{
    public async Task<DBus显示传输> 连接内嵌显示(
        虚拟机配置 vm,
        Action<DBus显示传输> 配置回调,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(配置回调);
        Session session;
        lock (gate)
        {
            if (!sessions.TryGetValue(vm.Id, out session!) || !session.IsActive)
                throw new InvalidOperationException(T("session.notRunning", "虚拟机没有运行"));
        }

        await session.DisplayConnectionGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!session.IsActive)
                throw new InvalidOperationException(T("session.notRunning", "虚拟机没有运行"));

            if (session.DisplayTransport is null)
            {
                var transport = new DBus显示传输();
                Exception? lastError = null;
                for (var attempt = 0; attempt < 30 && session.IsActive; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        await transport.连接(
                            session.Process.Id,
                            (command, arguments, token) => 执行QMP(vm, command, arguments, token),
                            cancellationToken);
                        lastError = null;
                        break;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        lastError = exception;
                        await Task.Delay(150, cancellationToken);
                    }
                }

                if (!transport.已连接)
                {
                    await transport.DisposeAsync();
                    throw new InvalidOperationException(
                        T("display.dbusConnectionFailed", "无法建立 QEMU D-Bus 内嵌显示连接。"), lastError);
                }
                if (!session.IsActive)
                {
                    await transport.DisposeAsync();
                    throw new InvalidOperationException(T("session.notRunning", "虚拟机没有运行"));
                }
                session.DisplayTransport = transport;
            }

            配置回调(session.DisplayTransport);
            if (!session.DisplayListenerRegistered)
            {
                await session.DisplayTransport.注册控制台(0, cancellationToken);
                session.DisplayListenerRegistered = true;
            }
            return session.DisplayTransport;
        }
        finally
        {
            session.DisplayConnectionGate.Release();
        }
    }

    public async Task 断开内嵌显示(虚拟机配置 vm)
    {
        Session? session;
        lock (gate) sessions.TryGetValue(vm.Id, out session);
        if (session is null) return;

        await session.DisplayConnectionGate.WaitAsync();
        try
        {
            var transport = session.DisplayTransport;
            session.DisplayTransport = null;
            session.DisplayListenerRegistered = false;
            if (transport is not null) await transport.DisposeAsync();
        }
        finally
        {
            session.DisplayConnectionGate.Release();
        }
    }
}
