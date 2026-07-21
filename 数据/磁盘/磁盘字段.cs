namespace QemuWG.数据;

public sealed class 磁盘字段
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public 磁盘字段模式 Mode { get; init; }
    public string Argument { get; init; } = string.Empty;
    public string DefaultValue { get; init; } = string.Empty;
    public IReadOnlyList<string> Choices { get; init; } = [];
    public bool Required { get; init; }
    public bool Wide { get; init; }
    public bool Advanced { get; init; }
    public 磁盘路径类型 PathKind { get; init; }
    public string DependsOnId { get; init; } = string.Empty;
    public string DependsOnValue { get; init; } = string.Empty;
    public bool InvertDependency { get; init; }
}
