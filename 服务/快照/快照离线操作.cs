using System.Text.Json;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class 快照服务
{
    private async Task<操作结果> 创建离线快照(
        仿真配置 vm,
        string tag,
        CancellationToken cancellationToken)
    {
        var images = await 获取离线镜像(vm, cancellationToken);
        var completed = new List<string>();
        foreach (var image in images)
        {
            var result = await 进程.运行(install.ImgToolPath, ["snapshot", "-c", tag, image], cancellationToken);
            if (result.退出码 == 0)
            {
                completed.Add(image);
                continue;
            }
            var rollbackErrors = new List<string>();
            foreach (var rollbackImage in completed)
            {
                var rollback = await 进程.运行(
                    install.ImgToolPath,
                    ["snapshot", "-d", tag, rollbackImage],
                    CancellationToken.None);
                if (rollback.退出码 != 0) rollbackErrors.Add($"{rollbackImage}: {rollback.输出}");
            }
            var detail = result.输出;
            if (rollbackErrors.Count > 0)
                detail += Environment.NewLine + "回滚残留：" + Environment.NewLine + string.Join(Environment.NewLine, rollbackErrors);
            return 操作结果.Fail("创建磁盘快照失败。", detail);
        }
        return 操作结果.Ok("磁盘快照已创建。");
    }

    private async Task<操作结果> 恢复离线快照(
        仿真配置 vm,
        string tag,
        CancellationToken cancellationToken)
    {
        var images = await 获取离线镜像(vm, cancellationToken);
        foreach (var image in images)
            if (!await 镜像包含标签(image, tag, cancellationToken))
                return 操作结果.Fail("快照不完整，已停止恢复。", $"设备缺少快照标签：{image}");

        var rollbackTag = $"qemuwg-rollback-{Guid.NewGuid():N}";
        var rollbackCreated = new List<string>();
        foreach (var image in images)
        {
            var created = await 进程.运行(install.ImgToolPath, ["snapshot", "-c", rollbackTag, image], cancellationToken);
            if (created.退出码 == 0)
            {
                rollbackCreated.Add(image);
                continue;
            }
            await 删除临时回滚快照(rollbackCreated, rollbackTag);
            return 操作结果.Fail("无法建立恢复保护点，未修改任何磁盘。", created.输出);
        }

        try
        {
            foreach (var image in images)
            {
                var applied = await 进程.运行(install.ImgToolPath, ["snapshot", "-a", tag, image], cancellationToken);
                if (applied.退出码 == 0) continue;
                var rollbackDetail = await 回滚离线恢复(images, rollbackTag);
                return 操作结果.Fail(
                    "恢复磁盘快照失败，已尝试回到操作前状态。",
                    applied.输出 + (rollbackDetail.Length == 0 ? string.Empty : Environment.NewLine + rollbackDetail));
            }
        }
        catch (OperationCanceledException)
        {
            var rollbackDetail = await 回滚离线恢复(images, rollbackTag);
            if (rollbackDetail.Length > 0)
                return 操作结果.Fail("恢复已取消，但回滚未完全成功。", rollbackDetail);
            throw;
        }

        var cleanup = await 删除临时回滚快照(images, rollbackTag);
        return cleanup.Length == 0
            ? 操作结果.Ok("磁盘快照已恢复。")
            : new 操作结果(true, "磁盘快照已恢复。", "临时保护点清理失败，可稍后重试：" + Environment.NewLine + cleanup);
    }

    private async Task<操作结果> 删除离线快照(
        仿真配置 vm,
        string tag,
        CancellationToken cancellationToken)
    {
        var images = await 获取离线镜像(vm, cancellationToken);
        var remaining = new List<string>();
        foreach (var image in images)
            if (await 镜像包含标签(image, tag, cancellationToken)) remaining.Add(image);
        if (remaining.Count == 0) return 操作结果.Fail("没有找到要删除的快照标签。");

        var deleted = new List<string>();
        foreach (var image in remaining)
        {
            var result = await 进程.运行(install.ImgToolPath, ["snapshot", "-d", tag, image], cancellationToken);
            if (result.退出码 == 0)
            {
                deleted.Add(image);
                continue;
            }
            var detail = result.输出;
            if (deleted.Count > 0)
                detail += Environment.NewLine + "部分设备已删除；再次执行删除会继续清理其余设备。";
            return 操作结果.Fail("删除磁盘快照失败。", detail);
        }
        return 操作结果.Ok("磁盘快照已删除。");
    }

    private async Task<bool> 镜像包含标签(string image, string tag, CancellationToken cancellationToken)
    {
        var result = await 进程.运行(
            install.ImgToolPath,
            ["info", "--output", "json", image],
            cancellationToken);
        if (result.退出码 != 0) throw new InvalidOperationException(result.输出);
        using var document = JsonDocument.Parse(result.输出);
        return document.RootElement.TryGetProperty("snapshots", out var snapshots)
               && snapshots.EnumerateArray().Any(snapshot =>
                   snapshot.TryGetProperty("name", out var name)
                   && string.Equals(name.GetString(), tag, StringComparison.Ordinal));
    }

    private async Task<string> 回滚离线恢复(IReadOnlyList<string> images, string rollbackTag)
    {
        var errors = new List<string>();
        foreach (var image in images)
        {
            var result = await 进程.运行(
                install.ImgToolPath,
                ["snapshot", "-a", rollbackTag, image],
                CancellationToken.None);
            if (result.退出码 != 0) errors.Add($"{image}: {result.输出}");
        }
        var cleanup = await 删除临时回滚快照(images, rollbackTag);
        if (cleanup.Length > 0) errors.Add(cleanup);
        return string.Join(Environment.NewLine, errors);
    }

    private async Task<string> 删除临时回滚快照(IEnumerable<string> images, string rollbackTag)
    {
        var errors = new List<string>();
        foreach (var image in images)
        {
            var result = await 进程.运行(
                install.ImgToolPath,
                ["snapshot", "-d", rollbackTag, image],
                CancellationToken.None);
            if (result.退出码 != 0) errors.Add($"{image}: {result.输出}");
        }
        return string.Join(Environment.NewLine, errors);
    }

    private async Task<IReadOnlyList<string>> 获取离线镜像(
        仿真配置 vm,
        CancellationToken cancellationToken)
    {
        验证镜像工具();
        var paths = new List<string> { vm.DiskPath };
        var variables = UEFI变量存储.获取路径(vm);
        var oldVariables = Path.Combine(vm.DirPath, "uefi-vars.fd");
        if (!File.Exists(variables) && File.Exists(oldVariables))
        {
            var preparation = UEFI变量存储.准备(install, vm, null);
            if (!preparation.Succeeded)
                throw new InvalidOperationException($"无法迁移 UEFI 变量存储：{preparation.Message} {preparation.Detail}".Trim());
        }
        if (File.Exists(variables)) paths.Add(variables);
        foreach (var path in paths)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("找不到快照磁盘镜像。", path);
            var result = await 进程.运行(
                install.ImgToolPath,
                ["info", "--output", "json", path],
                cancellationToken);
            if (result.退出码 != 0) throw new InvalidOperationException(result.输出);
            using var document = JsonDocument.Parse(result.输出);
            if (!document.RootElement.TryGetProperty("format", out var format)
                || !string.Equals(format.GetString(), "qcow2", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"磁盘不是 QCOW2 格式，不能创建内部快照：{path}");
        }
        return paths;
    }

    private void 验证镜像工具()
    {
        if (!File.Exists(install.ImgToolPath))
            throw new FileNotFoundException("未找到 qemu-img。", install.ImgToolPath);
    }
}
