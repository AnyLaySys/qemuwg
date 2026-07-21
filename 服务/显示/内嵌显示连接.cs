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

        var requestVersion = Interlocked.Increment(ref session.DisplayRequestVersion);
        await session.DisplayConnectionGate.WaitAsync(cancellationToken);
        DBus显示传输? createdTransport = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (requestVersion != Volatile.Read(ref session.DisplayRequestVersion))
                throw new OperationCanceledException(cancellationToken);
            if (!session.IsActive)
                throw new InvalidOperationException(T("session.notRunning", "虚拟机没有运行"));

            if (session.DisplayTransport is null)
            {
                createdTransport = new DBus显示传输();
                Exception? lastError = null;
                for (var attempt = 0; attempt < 30 && session.IsActive; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (requestVersion != Volatile.Read(ref session.DisplayRequestVersion))
                        throw new OperationCanceledException(cancellationToken);
                    try
                    {
                        await createdTransport.连接(
                            session.Process.Id,
                            (requests, token) => 执行QMP批次(vm, requests, token),
                            cancellationToken);
                        lastError = null;
                        break;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        lastError = exception;
                        if (!应重试显示连接(exception)) break;
                        var retryDelay = TimeSpan.FromMilliseconds(Math.Min(100, 10 * (attempt + 1)));
                        await Task.Delay(retryDelay, cancellationToken);
                    }
                }

                if (!createdTransport.已连接)
                    throw new InvalidOperationException(
                        T("display.dbusConnectionFailed", "无法建立 QEMU D-Bus 内嵌显示连接。"), lastError);
                if (!session.IsActive)
                    throw new InvalidOperationException(T("session.notRunning", "虚拟机没有运行"));
                cancellationToken.ThrowIfCancellationRequested();
                if (requestVersion != Volatile.Read(ref session.DisplayRequestVersion))
                    throw new OperationCanceledException(cancellationToken);
                session.DisplayTransport = createdTransport;
                createdTransport = null;
            }

            配置回调(session.DisplayTransport);
            if (!session.DisplayListenerRegistered)
            {
                await session.DisplayTransport.注册控制台(0, cancellationToken);
                session.DisplayListenerRegistered = true;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (requestVersion != Volatile.Read(ref session.DisplayRequestVersion))
                throw new OperationCanceledException(cancellationToken);
            return session.DisplayTransport;
        }
        catch
        {
            var failedTransport = session.DisplayTransport;
            session.DisplayTransport = null;
            session.DisplayListenerRegistered = false;
            if (failedTransport is not null) await failedTransport.DisposeAsync();
            throw;
        }
        finally
        {
            try
            {
                if (createdTransport is not null) await createdTransport.DisposeAsync();
            }
            finally
            {
                session.DisplayConnectionGate.Release();
            }
        }
    }

    private static bool 应重试显示连接(Exception exception)
    {
        if (exception is not QMP显示连接异常 qmpError) return false;
        return qmpError.错误类别 is
            "SocketException" or
            "IOException" or
            "EndOfStreamException" or
            "ConnectionClosed" or
            "Timeout";
    }

    public async Task 断开内嵌显示(虚拟机配置 vm)
    {
        Session? session;
        lock (gate) sessions.TryGetValue(vm.Id, out session);
        if (session is null) return;

        var requestVersion = Interlocked.Increment(ref session.DisplayRequestVersion);
        await session.DisplayConnectionGate.WaitAsync();
        try
        {
            if (requestVersion != Volatile.Read(ref session.DisplayRequestVersion)) return;
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

    private async Task 清理内嵌显示(Session session)
    {
        Interlocked.Increment(ref session.DisplayRequestVersion);
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
