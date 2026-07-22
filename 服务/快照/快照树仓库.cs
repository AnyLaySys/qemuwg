using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed class 快照树仓库
{
    private const int 当前版本 = 1;
    private const string 文件名 = "snapshots.json";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> 写入门控 =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions Json选项 = new()
    {
        WriteIndented = true
    };

    private static readonly UTF8Encoding UTF8无签名 = new(false);

    public async Task<快照树状态> 加载(
        仿真配置 vm,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vm);
        if (string.IsNullOrWhiteSpace(vm.DirPath)) return 新建状态();

        var path = 获取路径(vm);
        var gate = 获取门控(path);
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await 加载核心(path, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<快照树状态> 记录创建(
        仿真配置 vm,
        快照元数据 metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        验证标签(metadata.Tag);

        return 修改(vm, state =>
        {
            var parentTag = string.IsNullOrWhiteSpace(metadata.ParentTag)
                ? state.CurrentParentTag
                : metadata.ParentTag.Trim();
            var node = 复制元数据(metadata, parentTag);
            var existingIndex = state.Nodes.FindIndex(item =>
                string.Equals(item.Tag, node.Tag, StringComparison.Ordinal));
            if (existingIndex >= 0)
                state.Nodes[existingIndex] = node;
            else
                state.Nodes.Add(node);
            state.CurrentParentTag = node.Tag;
        }, cancellationToken);
    }

    public Task<快照树状态> 记录恢复(
        仿真配置 vm,
        string tag,
        CancellationToken cancellationToken = default)
    {
        验证标签(tag);
        var normalizedTag = tag.Trim();
        return 修改(vm, state => state.CurrentParentTag = normalizedTag, cancellationToken);
    }

    public Task<快照树状态> 记录删除(
        仿真配置 vm,
        string tag,
        CancellationToken cancellationToken = default)
    {
        验证标签(tag);
        var normalizedTag = tag.Trim();
        return 修改(vm, state =>
        {
            var removed = state.Nodes.FirstOrDefault(item =>
                string.Equals(item.Tag, normalizedTag, StringComparison.Ordinal));
            var parentTag = removed?.ParentTag ?? string.Empty;
            if (removed is not null)
            {
                state.Nodes.Remove(removed);
                foreach (var child in state.Nodes.Where(item =>
                             string.Equals(item.ParentTag, normalizedTag, StringComparison.Ordinal)))
                    child.ParentTag = parentTag;
            }

            if (string.Equals(state.CurrentParentTag, normalizedTag, StringComparison.Ordinal))
                state.CurrentParentTag = parentTag;
        }, cancellationToken);
    }

    private static async Task<快照树状态> 修改(
        仿真配置 vm,
        Action<快照树状态> change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentNullException.ThrowIfNull(change);
        if (string.IsNullOrWhiteSpace(vm.DirPath))
            throw new InvalidOperationException("仿真目录不能为空。");

        var path = 获取路径(vm);
        var gate = 获取门控(path);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await 加载核心(path, cancellationToken);
            change(state);
            规范化(state);
            await 原子保存(path, state, cancellationToken);
            return state;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<快照树状态> 加载核心(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return 新建状态();
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var state = await JsonSerializer.DeserializeAsync<快照树状态>(
                stream,
                Json选项,
                cancellationToken);
            if (state is null) return 新建状态();
            规范化(state);
            return state;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or JsonException
                                           or NotSupportedException)
        {
            throw new InvalidDataException("无法读取快照树元数据；为避免覆盖名称、说明和分支关系，已停止本次操作。", exception);
        }
    }

    private static async Task 原子保存(
        string path,
        快照树状态 state,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException("无法确定快照元数据目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(state, Json选项);
            await File.WriteAllTextAsync(temporaryPath, json, UTF8无签名, cancellationToken);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void 规范化(快照树状态 state)
    {
        state.Version = 当前版本;
        state.CurrentParentTag = state.CurrentParentTag?.Trim() ?? string.Empty;
        state.Nodes ??= [];

        var uniqueNodes = new List<快照元数据>(state.Nodes.Count);
        var seenTags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in state.Nodes)
        {
            if (node is null || string.IsNullOrWhiteSpace(node.Tag)) continue;
            var normalized = 复制元数据(node, node.ParentTag);
            if (seenTags.Add(normalized.Tag)) uniqueNodes.Add(normalized);
        }
        state.Nodes = uniqueNodes;
    }

    private static 快照元数据 复制元数据(快照元数据 source, string parentTag) => new()
    {
        Tag = source.Tag.Trim(),
        Name = source.Name?.Trim() ?? string.Empty,
        Description = source.Description?.Trim() ?? string.Empty,
        ParentTag = parentTag?.Trim() ?? string.Empty,
        ConfigurationFingerprint = source.ConfigurationFingerprint?.Trim() ?? string.Empty,
        CreatedAt = source.CreatedAt == default ? DateTimeOffset.UtcNow : source.CreatedAt
    };

    private static void 验证标签(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            throw new ArgumentException("快照标签不能为空。", nameof(tag));
    }

    private static 快照树状态 新建状态() => new() { Version = 当前版本 };

    private static string 获取路径(仿真配置 vm) => Path.Combine(vm.DirPath, 文件名);

    private static SemaphoreSlim 获取门控(string path) =>
        写入门控.GetOrAdd(Path.GetFullPath(path), static _ => new SemaphoreSlim(1, 1));
}
