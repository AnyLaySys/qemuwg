using QemuWG.数据;

namespace QemuWG.服务;

public static partial class QEMU镜像命令
{
    private static 磁盘字段 Text(string id, string label, string argument = "", string defaultValue = "",
        bool positional = false, bool required = false, bool wide = false, 磁盘字段模式? mode = null, bool advanced = false) => new()
    {
        Id = id,
        Label = label,
        Argument = argument,
        DefaultValue = defaultValue,
        Required = required,
        Wide = wide,
        Advanced = advanced,
        Mode = mode ?? (positional ? 磁盘字段模式.Positional : 磁盘字段模式.OptionValue)
    };

    private static 磁盘字段 ConditionalText(string id, string label, string dependsOn, string dependsValue, bool invert) => new()
    {
        Id = id,
        Label = label,
        Mode = 磁盘字段模式.Positional,
        DependsOnId = dependsOn,
        DependsOnValue = dependsValue,
        InvertDependency = invert
    };

    private static 磁盘字段 Path(string id, string label, bool required, bool save, string argument = "") => new()
    {
        Id = id,
        Label = label,
        Argument = argument,
        Required = required,
        Wide = true,
        PathKind = save ? 磁盘路径类型.Save : 磁盘路径类型.Open,
        Mode = string.IsNullOrEmpty(argument) ? 磁盘字段模式.Positional : 磁盘字段模式.OptionValue
    };

    private static 磁盘字段 MultiPath(string id, string label, bool required) => new()
    {
        Id = id,
        Label = label,
        Required = required,
        Wide = true,
        PathKind = 磁盘路径类型.Open,
        Mode = 磁盘字段模式.MultiPositional
    };

    private static 磁盘字段 AssignmentPath(string id, string label, string prefix, bool required, bool save) => new()
    {
        Id = id,
        Label = label,
        Argument = prefix,
        Required = required,
        Wide = true,
        PathKind = save ? 磁盘路径类型.Save : 磁盘路径类型.Open,
        Mode = 磁盘字段模式.Assignment
    };

    private static 磁盘字段 Assignment(string id, string label, string prefix, string defaultValue = "") => new()
    {
        Id = id,
        Label = label,
        Argument = prefix,
        DefaultValue = defaultValue,
        Mode = 磁盘字段模式.Assignment
    };

    private static 磁盘字段 Toggle(string id, string argument) => new()
    {
        Id = id,
        Label = argument,
        Argument = argument,
        Mode = 磁盘字段模式.Flag
    };

    private static 磁盘字段 Choice(string id, string label, string argument, string defaultValue, params string[] choices) => new()
    {
        Id = id,
        Label = label,
        Argument = argument,
        DefaultValue = defaultValue,
        Choices = choices,
        Mode = 磁盘字段模式.OptionValue
    };

    private static 磁盘字段 Cache(string id, string label, string argument, string defaultValue) =>
        Choice(id, label, argument, defaultValue, "writeback", "none", "writethrough", "directsync", "unsafe");

    private static 磁盘字段 Action(string id, string label, string defaultValue, params string[] choices) => new()
    {
        Id = id,
        Label = label,
        DefaultValue = defaultValue,
        Choices = [defaultValue, .. choices],
        Mode = 磁盘字段模式.ChoiceArguments
    };
}
