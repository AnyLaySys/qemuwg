using System.Collections.Concurrent;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class 快照服务
{
    private static string T(string key, string fallback) => 语言服务.当前.获取(key, fallback);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> 操作门控 =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly QEMU安装 install;
    private readonly QEMU会话 sessions;
    private readonly 快照树仓库 repository = new();

    public 快照服务(QEMU安装 install, QEMU会话 sessions)
    {
        this.install = install ?? throw new ArgumentNullException(nameof(install));
        this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public Task<操作结果> 创建(
        仿真配置 vm,
        string name,
        string description,
        CancellationToken cancellationToken = default) =>
        串行执行(vm, token => 创建核心(vm, name, description, token), cancellationToken);

    public Task<操作结果> 恢复(
        仿真配置 vm,
        快照信息 snapshot,
        CancellationToken cancellationToken = default) =>
        串行执行(vm, token => 恢复核心(vm, snapshot, token), cancellationToken);

    public Task<操作结果> 删除(
        仿真配置 vm,
        快照信息 snapshot,
        CancellationToken cancellationToken = default) =>
        串行执行(vm, token => 删除核心(vm, snapshot, token), cancellationToken);

    private static async Task<操作结果> 串行执行(
        仿真配置 vm,
        Func<CancellationToken, Task<操作结果>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vm);
        var key = string.IsNullOrWhiteSpace(vm.Id) ? Path.GetFullPath(vm.DirPath) : vm.Id;
        var gate = 操作门控.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await operation(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return 操作结果.Fail(T("snapshot.service.operationFailed", "快照操作失败。"), exception.Message);
        }
        finally
        {
            gate.Release();
        }
    }
}
