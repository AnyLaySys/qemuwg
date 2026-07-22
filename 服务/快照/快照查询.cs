using System.Text.Json;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class 快照服务
{
    public async Task<快照查询结果> 查询(
        仿真配置 vm,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vm);
        try
        {
            var snapshots = sessions.存在QMP会话(vm)
                ? await 查询运行中快照(vm, cancellationToken)
                : await 查询离线快照(vm, cancellationToken);
            var tree = await repository.加载(vm, cancellationToken);
            return new 快照查询结果(
                true,
                $"找到 {snapshots.Count} 个快照。",
                合并元数据(vm, snapshots, tree),
                tree.CurrentParentTag);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new 快照查询结果(false, exception.Message, [], string.Empty);
        }
    }

    private async Task<IReadOnlyList<快照信息>> 查询运行中快照(
        仿真配置 vm,
        CancellationToken cancellationToken)
    {
        var result = await sessions.执行QMP(vm, "query-named-block-nodes", cancellationToken: cancellationToken);
        if (!result.Succeeded)
            throw new InvalidOperationException("无法读取 QEMU 快照：" + 格式化QMP错误(result));
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
        var writableQcow2 = writableTopLevel.Where(node =>
                node.TryGetProperty("drv", out var driver)
                && string.Equals(driver.GetString(), "qcow2", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var systemPath = Path.GetFullPath(vm.DiskPath);
        var systemNode = writableQcow2.FirstOrDefault(node => 节点匹配路径(node, systemPath));
        if (systemNode.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("无法按系统磁盘路径定位 QEMU 块节点。");
        var orderedNodes = new[] { systemNode }
            .Concat(writableQcow2.Where(node =>
                !string.Equals(
                    node.GetProperty("node-name").GetString(),
                    systemNode.GetProperty("node-name").GetString(),
                    StringComparison.Ordinal)))
            .ToArray();
        var deviceSnapshots = orderedNodes.Select(读取节点快照).ToArray();
        var unsupported = writableTopLevel.Length - writableQcow2.Length;
        var issue = unsupported > 0
            ? $"当前还有 {unsupported} 个顶层可写设备不支持内部快照，无法保证恢复一致性。"
            : string.Empty;
        return 聚合设备快照(deviceSnapshots, issue);
    }

    private async Task<IReadOnlyList<快照信息>> 查询离线快照(
        仿真配置 vm,
        CancellationToken cancellationToken)
    {
        var images = await 获取离线镜像(vm, cancellationToken);
        var deviceSnapshots = new List<IReadOnlyList<快照信息>>(images.Count);
        foreach (var image in images)
        {
            var result = await 进程.运行(
                install.ImgToolPath,
                ["info", "--output", "json", image],
                cancellationToken);
            if (result.退出码 != 0) throw new InvalidOperationException(result.输出);
            using var document = JsonDocument.Parse(result.输出);
            deviceSnapshots.Add(document.RootElement.TryGetProperty("snapshots", out var snapshots)
                ? snapshots.EnumerateArray().Select(解析快照).ToArray()
                : []);
        }
        return 聚合设备快照(deviceSnapshots, string.Empty);
    }

    private static IReadOnlyList<快照信息> 读取节点快照(JsonElement node)
    {
        if (!node.TryGetProperty("image", out var image)
            || !image.TryGetProperty("snapshots", out var snapshots)) return [];
        return snapshots.EnumerateArray().Select(解析快照).ToArray();
    }

    private static IReadOnlyList<快照信息> 聚合设备快照(
        IReadOnlyList<IReadOnlyList<快照信息>> deviceSnapshots,
        string deviceIssue)
    {
        if (deviceSnapshots.Count == 0) return [];
        var maps = deviceSnapshots.Select(list => list
                .Where(snapshot => !string.IsNullOrWhiteSpace(snapshot.Tag))
                .GroupBy(snapshot => snapshot.Tag, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal))
            .ToArray();
        var tags = maps.SelectMany(map => map.Keys).Distinct(StringComparer.Ordinal).ToArray();
        return tags.Select(tag =>
            {
                var facts = maps.Where(map => map.ContainsKey(tag)).Select(map => map[tag]).ToArray();
                var source = maps[0].GetValueOrDefault(tag) ?? facts[0];
                var missing = maps.Count(map => !map.ContainsKey(tag));
                var problems = new List<string>();
                if (missing > 0)
                    problems.Add($"该标签缺少 {missing} 个参与设备的快照，不能安全恢复。");
                if (!string.IsNullOrWhiteSpace(deviceIssue)) problems.Add(deviceIssue);
                return new 快照信息
                {
                    Id = source.Id,
                    Tag = tag,
                    Name = source.Name,
                    CreatedAt = facts.Where(item => item.CreatedAt != default)
                        .Select(item => item.CreatedAt)
                        .DefaultIfEmpty(source.CreatedAt)
                        .Min(),
                    VmStateSize = facts.Max(item => item.VmStateSize),
                    VmClock = facts.Select(item => item.VmClock).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty,
                    可用 = problems.Count == 0,
                    问题 = string.Join(Environment.NewLine, problems)
                };
            })
            .OrderBy(snapshot => snapshot.CreatedAt)
            .ToArray();
    }

    private static IReadOnlyList<快照信息> 合并元数据(
        仿真配置 vm,
        IReadOnlyList<快照信息> snapshots,
        快照树状态 tree)
    {
        var metadata = tree.Nodes.ToDictionary(node => node.Tag, StringComparer.Ordinal);
        return snapshots.Select(snapshot =>
        {
            if (!metadata.TryGetValue(snapshot.Tag, out var node)) return snapshot;
            var configurationChanged = snapshot.含内存
                                       && !string.IsNullOrWhiteSpace(node.ConfigurationFingerprint)
                                       && !string.Equals(node.ConfigurationFingerprint, 计算配置签名(vm), StringComparison.Ordinal);
            var problems = new List<string>();
            if (!string.IsNullOrWhiteSpace(snapshot.问题)) problems.Add(snapshot.问题);
            if (configurationChanged) problems.Add("创建完整状态快照后，仿真硬件配置已发生变化。");
            return new 快照信息
            {
                Id = snapshot.Id,
                Tag = snapshot.Tag,
                Name = string.IsNullOrWhiteSpace(node.Name) ? snapshot.Name : node.Name,
                Description = node.Description,
                ParentTag = node.ParentTag,
                ConfigurationFingerprint = node.ConfigurationFingerprint,
                CreatedAt = node.CreatedAt == default ? snapshot.CreatedAt : node.CreatedAt,
                VmStateSize = snapshot.VmStateSize,
                VmClock = snapshot.VmClock,
                可用 = snapshot.可用 && !configurationChanged,
                问题 = string.Join(Environment.NewLine, problems)
            };
        }).ToArray();
    }

    private static 快照信息 解析快照(JsonElement snapshot)
    {
        var tag = 获取字符串(snapshot, "name");
        var dateSeconds = 获取长整数(snapshot, "date-sec");
        var dateNanoseconds = 获取长整数(snapshot, "date-nsec");
        DateTimeOffset createdAt = default;
        if (dateSeconds > 0)
            createdAt = DateTimeOffset.FromUnixTimeSeconds(dateSeconds).AddTicks(dateNanoseconds / 100);
        var clockSeconds = 获取长整数(snapshot, "vm-clock-sec");
        var clockNanoseconds = 获取长整数(snapshot, "vm-clock-nsec");
        var clock = TimeSpan.FromSeconds(clockSeconds) + TimeSpan.FromTicks(clockNanoseconds / 100);
        return new 快照信息
        {
            Id = 获取字符串(snapshot, "id"),
            Tag = tag,
            Name = tag,
            CreatedAt = createdAt,
            VmStateSize = 获取长整数(snapshot, "vm-state-size"),
            VmClock = clock == TimeSpan.Zero ? string.Empty : clock.ToString()
        };
    }

    private static string 获取字符串(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.ToString() : string.Empty;

    private static long 获取长整数(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt64(out var number) ? number : 0;
}
