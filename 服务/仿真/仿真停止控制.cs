using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU会话
{
    public async Task<操作结果> 关机(仿真配置 vm)
    {
        var result = await 执行QMP(vm, "system_powerdown");
        return result.Succeeded
            ? 操作结果.Ok(T("session.shutdownRequested", "已发送关机请求"))
            : 操作结果.Fail(T("session.shutdownFailed", "发送关机请求失败"), result.Output);
    }

    public async Task<bool> 等待退出(仿真配置 vm, TimeSpan timeout, CancellationToken cancellationToken = default)
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

    public 操作结果 强制停止(仿真配置 vm)
    {
        Session? session;
        lock (gate) sessions.TryGetValue(vm.Id, out session);
        if (session is null || !session.IsActive) return 操作结果.Fail(T("session.notRunning", "仿真没有运行"));
        try
        {
            session.Process.Kill(true);
            return 操作结果.Ok(T("session.forceStopped", "仿真已强制停止"));
        }
        catch (Exception exception)
        {
            return 操作结果.Fail(T("session.stopFailed", "无法停止仿真"), exception.Message);
        }
    }
}
