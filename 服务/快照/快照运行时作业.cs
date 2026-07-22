using System.Text.Json;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class 快照服务
{
    private static readonly TimeSpan 作业总时限 = TimeSpan.FromMinutes(5);

    private async Task<操作结果> 执行快照作业(
        仿真配置 vm,
        string command,
        string tag,
        运行中节点 nodes,
        string? vmstate,
        CancellationToken cancellationToken)
    {
        var jobId = $"qemuwg-{command}-{Guid.NewGuid():N}";
        var arguments = new Dictionary<string, object?>
        {
            ["job-id"] = jobId,
            ["tag"] = tag,
            ["devices"] = nodes.Devices
        };
        if (vmstate is not null) arguments["vmstate"] = vmstate;

        using var timeout = new CancellationTokenSource(作业总时限);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var token = linked.Token;
        var previousJob = await 检查遗留快照作业(vm, token);
        if (previousJob is not null) return previousJob;
        var started = await sessions.执行QMP(vm, command, JsonSerializer.Serialize(arguments), token);
        if (!started.Succeeded)
            return 操作结果.Fail("QEMU 快照作业启动失败。", 格式化QMP错误(started));

        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var jobsResult = await sessions.执行QMP(vm, "query-jobs", cancellationToken: token);
                if (!jobsResult.Succeeded)
                {
                    await 尝试取消并清理作业(vm, jobId);
                    return 操作结果.Fail("无法查询 QEMU 快照作业状态。", 格式化QMP错误(jobsResult));
                }

                using var document = JsonDocument.Parse(jobsResult.Output);
                var job = document.RootElement.EnumerateArray().FirstOrDefault(item =>
                    item.TryGetProperty("id", out var id)
                    && string.Equals(id.GetString(), jobId, StringComparison.Ordinal));
                if (job.ValueKind == JsonValueKind.Undefined)
                    return 操作结果.Fail("QEMU 快照作业意外消失。");

                var status = job.TryGetProperty("status", out var statusValue)
                    ? statusValue.GetString()
                    : string.Empty;
                if (string.Equals(status, "concluded", StringComparison.Ordinal))
                {
                    var error = job.TryGetProperty("error", out var errorValue)
                        ? errorValue.GetString()
                        : null;
                    var dismiss = await sessions.执行QMP(
                        vm,
                        "job-dismiss",
                        JsonSerializer.Serialize(new Dictionary<string, string> { ["id"] = jobId }),
                        token);
                    if (!string.IsNullOrWhiteSpace(error))
                        return 操作结果.Fail("QEMU 快照作业失败。", error);
                    if (!dismiss.Succeeded)
                        return 操作结果.Fail("快照作业已完成，但无法清理作业状态。", 格式化QMP错误(dismiss));
                    return 操作结果.Ok("QEMU 快照作业已完成。");
                }
                if (string.Equals(status, "null", StringComparison.Ordinal)
                    || string.Equals(status, "aborting", StringComparison.Ordinal))
                {
                    await 尝试取消并清理作业(vm, jobId);
                    return 操作结果.Fail($"QEMU 快照作业以异常状态结束：{status}");
                }

                await Task.Delay(150, token);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await 尝试取消并清理作业(vm, jobId);
            return 操作结果.Fail("QEMU 快照作业在五分钟内没有完成。");
        }
        catch (OperationCanceledException)
        {
            await 尝试取消并清理作业(vm, jobId);
            throw;
        }
    }

    private async Task<操作结果?> 检查遗留快照作业(仿真配置 vm, CancellationToken cancellationToken)
    {
        var result = await sessions.执行QMP(vm, "query-jobs", cancellationToken: cancellationToken);
        if (!result.Succeeded)
            return 操作结果.Fail("无法确认是否存在遗留的 QEMU 快照作业。", 格式化QMP错误(result));
        using var document = JsonDocument.Parse(result.Output);
        foreach (var job in document.RootElement.EnumerateArray())
        {
            var id = job.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(id) || !id.StartsWith("qemuwg-snapshot-", StringComparison.Ordinal)) continue;
            var status = job.TryGetProperty("status", out var statusValue) ? statusValue.GetString() : string.Empty;
            if (string.Equals(status, "concluded", StringComparison.Ordinal))
            {
                var dismissed = await sessions.执行QMP(
                    vm,
                    "job-dismiss",
                    JsonSerializer.Serialize(new Dictionary<string, string> { ["id"] = id }),
                    cancellationToken);
                if (!dismissed.Succeeded)
                    return 操作结果.Fail("无法清理上一次 QEMU 快照作业。", 格式化QMP错误(dismissed));
                continue;
            }
            return 操作结果.Fail($"上一次 QEMU 快照作业仍在运行（{status}），暂不能开始新操作。");
        }
        return null;
    }

    private async Task 尝试取消并清理作业(仿真配置 vm, string jobId)
    {
        try
        {
            await sessions.执行QMP(
                vm,
                "job-cancel",
                JsonSerializer.Serialize(new Dictionary<string, object> { ["id"] = jobId, ["force"] = true }),
                CancellationToken.None);
            for (var attempt = 0; attempt < 25; attempt++)
            {
                var jobs = await sessions.执行QMP(vm, "query-jobs", cancellationToken: CancellationToken.None);
                if (!jobs.Succeeded) return;
                using var document = JsonDocument.Parse(jobs.Output);
                var job = document.RootElement.EnumerateArray().FirstOrDefault(item =>
                    item.TryGetProperty("id", out var id) && string.Equals(id.GetString(), jobId, StringComparison.Ordinal));
                if (job.ValueKind == JsonValueKind.Undefined) return;
                var status = job.TryGetProperty("status", out var statusValue) ? statusValue.GetString() : string.Empty;
                if (string.Equals(status, "concluded", StringComparison.Ordinal))
                {
                    await sessions.执行QMP(
                        vm,
                        "job-dismiss",
                        JsonSerializer.Serialize(new Dictionary<string, string> { ["id"] = jobId }),
                        CancellationToken.None);
                    return;
                }
                await Task.Delay(200);
            }
        }
        catch
        {
        }
    }

    private async Task<运行中节点> 获取运行中节点(
        仿真配置 vm,
        CancellationToken cancellationToken,
        bool 要求所有可写设备可快照 = true)
    {
        var result = await sessions.执行QMP(vm, "query-named-block-nodes", cancellationToken: cancellationToken);
        if (!result.Succeeded)
            throw new InvalidOperationException("无法读取 QEMU 块设备：" + 格式化QMP错误(result));

        using var document = JsonDocument.Parse(result.Output);
        var nodes = document.RootElement.EnumerateArray().ToArray();
        var childNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (!node.TryGetProperty("children", out var children)) continue;
            foreach (var child in children.EnumerateArray())
                if (child.TryGetProperty("node-name", out var childName)
                    && !string.IsNullOrWhiteSpace(childName.GetString()))
                    childNames.Add(childName.GetString()!);
        }

        var writableTopLevel = nodes.Where(node =>
                (!node.TryGetProperty("ro", out var readOnly) || !readOnly.GetBoolean())
                && node.TryGetProperty("node-name", out var nodeName)
                && !childNames.Contains(nodeName.GetString() ?? string.Empty))
            .ToArray();
        var unsupported = writableTopLevel.FirstOrDefault(node =>
            !node.TryGetProperty("drv", out var driver)
            || !string.Equals(driver.GetString(), "qcow2", StringComparison.OrdinalIgnoreCase));
        if (要求所有可写设备可快照 && unsupported.ValueKind != JsonValueKind.Undefined)
        {
            var driver = unsupported.TryGetProperty("drv", out var driverValue)
                ? driverValue.GetString()
                : "unknown";
            throw new InvalidOperationException($"顶层可写块设备不是 QCOW2 格式，不能创建一致快照：{driver}");
        }

        var writableQcow2 = writableTopLevel.Where(node =>
                node.TryGetProperty("drv", out var driver)
                && string.Equals(driver.GetString(), "qcow2", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var devices = writableQcow2
            .Select(node => node.GetProperty("node-name").GetString()!)
            .ToArray();
        if (devices.Length == 0)
            throw new InvalidOperationException("没有找到可写的顶层 QCOW2 块设备。");

        var systemPath = Path.GetFullPath(vm.DiskPath);
        var systemNode = writableQcow2.FirstOrDefault(node => 节点匹配路径(node, systemPath));
        if (systemNode.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("无法按系统磁盘路径定位 QEMU 块节点。");
        return new 运行中节点(devices, systemNode.GetProperty("node-name").GetString()!);
    }

    private static bool 节点匹配路径(JsonElement node, string expectedFullPath)
    {
        foreach (var candidate in 枚举节点路径(node))
        {
            try
            {
                if (string.Equals(Path.GetFullPath(candidate), expectedFullPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
            }
        }
        return false;
    }

    private static IEnumerable<string> 枚举节点路径(JsonElement node)
    {
        if (node.TryGetProperty("file", out var file) && file.ValueKind == JsonValueKind.String)
            yield return file.GetString()!;
        if (node.TryGetProperty("image", out var image)
            && image.TryGetProperty("filename", out var filename)
            && filename.ValueKind == JsonValueKind.String)
            yield return filename.GetString()!;
    }

    private static string 格式化QMP错误(QMP结果 result) =>
        string.IsNullOrWhiteSpace(result.ErrorClass)
            ? result.Output
            : $"{result.ErrorClass}: {result.Output}";

    private sealed record 运行中节点(IReadOnlyList<string> Devices, string SystemNode);
}
