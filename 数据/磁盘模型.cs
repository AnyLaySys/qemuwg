using Microsoft.UI.Xaml.Media;

namespace QemuWG.数据;

public enum 磁盘字段模式
{
    Positional,
    MultiPositional,
    OptionValue,
    MultiOptionValue,
    Flag,
    Assignment,
    ChoiceArguments,
    GlobalOptionValue,
    RawArguments
}

public enum 磁盘路径类型
{
    None,
    Open,
    Save
}

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

public sealed class 磁盘命令
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string DisplayCategory { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Glyph { get; init; } = string.Empty;
    public Brush? IconBrush { get; init; }
    public bool CanWrite { get; init; }
    public IReadOnlyList<磁盘字段> Fields { get; init; } = [];
}

public sealed record 磁盘命令参数(
    IReadOnlyList<string> GlobalArgs,
    IReadOnlyList<string> CmdArgs,
    string Preview);

public sealed class 磁盘镜像信息
{
    public string Format { get; init; } = "unknown";
    public long VirtualSize { get; init; }
    public long ActualSize { get; init; }
    public string BackingFile { get; init; } = string.Empty;
}
