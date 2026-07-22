using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class 快照服务
{
    private async Task<操作结果> 创建核心(
        仿真配置 vm,
        string name,
        string description,
        CancellationToken cancellationToken)
    {
        var validation = 验证可修改(vm);
        if (validation is not null) return validation;
        if (string.IsNullOrWhiteSpace(name)) return 操作结果.Fail(T("snapshot.service.nameRequired", "快照名称不能为空。"));

        var tag = $"qemuwg-{Guid.NewGuid():N}";
        var running = sessions.存在QMP会话(vm);
        操作结果 operation;
        if (running)
        {
            var nodes = await 获取运行中节点(vm, cancellationToken);
            operation = await 执行快照作业(
                vm,
                "snapshot-save",
                tag,
                nodes,
                nodes.SystemNode,
                cancellationToken);
        }
        else
        {
            operation = await 创建离线快照(vm, tag, cancellationToken);
        }

        if (!operation.Succeeded) return operation;
        var tree = await repository.加载(vm, cancellationToken);
        await repository.记录创建(vm, new 快照元数据
        {
            Tag = tag,
            Name = name.Trim(),
            Description = description?.Trim() ?? string.Empty,
            ParentTag = tree.CurrentParentTag,
            ConfigurationFingerprint = 计算配置签名(vm),
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        return 操作结果.Ok(running
            ? T("snapshot.service.createdFull", "已创建包含内存状态的快照。")
            : T("snapshot.service.createdDisk", "已创建磁盘快照。"));
    }

    private async Task<操作结果> 恢复核心(
        仿真配置 vm,
        快照信息 snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var validation = 验证可修改(vm);
        if (validation is not null) return validation;
        if (string.IsNullOrWhiteSpace(snapshot.Tag)) return 操作结果.Fail(T("snapshot.service.tagRequired", "快照标签不能为空。"));
        if (snapshot.含内存
            && !string.IsNullOrWhiteSpace(snapshot.ConfigurationFingerprint)
            && !string.Equals(snapshot.ConfigurationFingerprint, 计算配置签名(vm), StringComparison.Ordinal))
            return 操作结果.Fail(T("snapshot.service.configurationChanged", "仿真硬件配置已在创建快照后发生变化。请恢复原配置后再恢复此完整状态快照。"));

        操作结果 operation;
        if (sessions.存在QMP会话(vm))
        {
            if (!snapshot.含内存)
                return 操作结果.Fail(T("snapshot.service.diskRestoreRequiresStopped", "磁盘快照只能在仿真关机后恢复，请先关机。"));
            var nodes = await 获取运行中节点(vm, cancellationToken);
            operation = await 执行快照作业(
                vm,
                "snapshot-load",
                snapshot.Tag,
                nodes,
                nodes.SystemNode,
                cancellationToken);
        }
        else if (snapshot.含内存)
        {
            operation = sessions.启动(install, vm, snapshot.Tag);
            if (operation.Succeeded)
                operation = await 等待完整快照启动(vm, cancellationToken);
        }
        else
        {
            operation = await 恢复离线快照(vm, snapshot.Tag, cancellationToken);
        }

        if (!operation.Succeeded) return operation;
        await repository.记录恢复(vm, snapshot.Tag, cancellationToken);
        return 操作结果.Ok(snapshot.含内存
            ? T("snapshot.service.restoredFull", "已恢复快照及内存状态。")
            : T("snapshot.service.restoredDisk", "已恢复磁盘快照。"));
    }

    private async Task<操作结果> 等待完整快照启动(
        仿真配置 vm,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sessions.存在QMP会话(vm))
            {
                var logPath = Path.Combine(vm.DirPath, "qemu.log");
                var detail = File.Exists(logPath)
                    ? string.Join(Environment.NewLine, File.ReadLines(logPath).TakeLast(12))
                    : string.Empty;
                return 操作结果.Fail(T("snapshot.service.fullRestoreStartFailed", "QEMU 未能从完整状态快照启动。"), detail);
            }

            var status = await sessions.执行QMP(vm, "query-status", cancellationToken: cancellationToken);
            if (status.Succeeded) return 操作结果.Ok(T("snapshot.service.fullRestoreLoaded", "完整状态快照已载入。"));
            await Task.Delay(300, cancellationToken);
        }
        return 操作结果.Fail(T("snapshot.service.fullRestoreTimeout", "QEMU 仍在载入完整状态快照，尚未确认恢复成功。请稍后刷新状态。"));
    }

    private async Task<操作结果> 删除核心(
        仿真配置 vm,
        快照信息 snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.Tag)) return 操作结果.Fail(T("snapshot.service.tagRequired", "快照标签不能为空。"));

        操作结果 operation;
        if (sessions.存在QMP会话(vm))
        {
            var nodes = await 获取运行中节点(vm, cancellationToken, false);
            operation = await 执行快照作业(
                vm,
                "snapshot-delete",
                snapshot.Tag,
                nodes,
                null,
                cancellationToken);
        }
        else
        {
            operation = await 删除离线快照(vm, snapshot.Tag, cancellationToken);
        }

        if (!operation.Succeeded) return operation;
        await repository.记录删除(vm, snapshot.Tag, cancellationToken);
        return 操作结果.Ok(T("snapshot.service.deleted", "快照已删除。"));
    }

    private static 操作结果? 验证可修改(仿真配置 vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        if (vm.SnapshotMode)
            return 操作结果.Fail(T("snapshot.service.temporaryModeBlocked", "一次性启动模式不会写回磁盘，不能管理持久快照。"));
        if (vm.PhysicalStorage.Any(storage => !storage.ReadOnly))
            return 操作结果.Fail(T("snapshot.service.physicalWriteBlocked", "挂载读写物理磁盘或分区时不能管理快照。"));
        return null;
    }
}
