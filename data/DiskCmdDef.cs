namespace QemuWG.data;

public enum DiskFieldMode
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

public enum DiskPathKind
{
    None,
    Open,
    Save
}

public sealed class DiskFieldDef
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public DiskFieldMode Mode { get; init; }
    public string Argument { get; init; } = string.Empty;
    public string DefaultValue { get; init; } = string.Empty;
    public IReadOnlyList<string> Choices { get; init; } = [];
    public bool Required { get; init; }
    public bool Wide { get; init; }
    public DiskPathKind PathKind { get; init; }
    public string DependsOnId { get; init; } = string.Empty;
    public string DependsOnValue { get; init; } = string.Empty;
    public bool InvertDependency { get; init; }
}

public sealed class DiskCmdDef
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string DisplayCategory { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool CanWrite { get; init; }
    public IReadOnlyList<DiskFieldDef> Fields { get; init; } = [];
}

public sealed record DiskCmdArgs(
    IReadOnlyList<string> GlobalArgs,
    IReadOnlyList<string> CmdArgs,
    string Preview);

public sealed class DiskImageInfo
{
    public string Format { get; init; } = "unknown";
    public long VirtualSize { get; init; }
    public long ActualSize { get; init; }
    public string BackingFile { get; init; } = string.Empty;
}

