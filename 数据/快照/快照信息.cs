namespace QemuWG.数据;

public sealed class 快照信息
{
    public string Id { get; init; } = string.Empty;
    public string Tag { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ParentTag { get; init; } = string.Empty;
    public string ConfigurationFingerprint { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public long VmStateSize { get; init; }
    public string VmClock { get; init; } = string.Empty;
    public bool 可用 { get; init; } = true;
    public string 问题 { get; init; } = string.Empty;

    public bool 含内存 => VmStateSize > 0;
}

public sealed record 快照查询结果(
    bool Succeeded,
    string Message,
    IReadOnlyList<快照信息> Snapshots,
    string CurrentParentTag);

public sealed class 快照树状态
{
    public int Version { get; set; } = 1;
    public string CurrentParentTag { get; set; } = string.Empty;
    public List<快照元数据> Nodes { get; set; } = [];
}

public sealed class 快照元数据
{
    public string Tag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ParentTag { get; set; } = string.Empty;
    public string ConfigurationFingerprint { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
