using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using QemuWG.数据;

namespace QemuWG.服务;

public static class QEMU镜像命令
{
    private static readonly 语言服务 本地化 = 语言服务.当前;

    public static IReadOnlyList<磁盘命令> 全部 { get; } =
    [
        Cmd("info", "检查", "显示镜像信息", false,
            Path("file", "镜像文件", true, false),
            Text("format", "格式", "-f"), Toggle("imageOpts", "--image-opts"),
            Toggle("backingChain", "--backing-chain"), Cache("cache", "缓存模式", "-t", "writeback"),
            Toggle("forceShare", "-U"), Toggle("limits", "--limits"),
            Choice("output", "输出格式", "--output", "human", "human", "json")),

        Cmd("check", "检查", "检查镜像完整性", true,
            Path("file", "镜像文件", true, false), Text("format", "格式", "-f"),
            Toggle("imageOpts", "--image-opts"), Cache("cache", "缓存模式", "-T", "writeback"),
            Choice("repair", "修复", "-r", "none", "none", "leaks", "all"), Toggle("forceShare", "-U"),
            Choice("output", "输出格式", "--output", "human", "human", "json"), Toggle("quiet", "-q")),

        Cmd("map", "检查", "输出镜像块映射", false,
            Path("file", "镜像文件", true, false), Text("format", "格式", "-f"),
            Toggle("imageOpts", "--image-opts"), Text("start", "起始偏移", "--start-offset"),
            Text("length", "最大长度", "--max-length"), Choice("output", "输出格式", "--output", "human", "human", "json"),
            Toggle("forceShare", "-U")),

        Cmd("measure", "检查", "计算目标镜像所需空间", false,
            Path("file", "源镜像", false, false), Text("size", "虚拟大小", "--size"),
            Text("format", "源格式", "-f"), Toggle("imageOpts", "--image-opts"), Text("snapshot", "快照", "-l"),
            Text("targetFormat", "目标格式", "-O", "qcow2"), Text("targetOptions", "目标格式选项", "-o", wide: true),
            Choice("output", "输出格式", "--output", "human", "human", "json"), Toggle("forceShare", "-U")),

        Cmd("compare", "检查", "比较两个镜像内容", false,
            Path("file1", "镜像 1", true, false), Path("file2", "镜像 2", true, false),
            Text("format1", "镜像 1 格式", "-f"), Text("format2", "镜像 2 格式", "-F"),
            Toggle("imageOpts", "--image-opts"), Toggle("strict", "-s"), Cache("cache", "缓存模式", "-T", "writeback"),
            Toggle("forceShare", "-U"), Toggle("progress", "-p"), Toggle("quiet", "-q")),

        Cmd("bench", "检查", "运行镜像 I/O 基准测试", true,
            Path("file", "镜像文件", true, false), Text("format", "格式", "-f"), Toggle("imageOpts", "--image-opts"),
            Cache("cache", "缓存模式", "-t", "writeback"), Text("count", "请求数量", "-c"),
            Text("depth", "并行深度", "-d"), Text("offset", "起始偏移", "-o"),
            Text("buffer", "缓冲区大小", "-s", "4K"), Text("step", "步长", "-S"),
            Toggle("write", "-w"), Text("pattern", "写入字节", "--pattern"),
            Text("flush", "刷新间隔", "--flush-interval"), Toggle("noDrain", "--no-drain"),
            Choice("aio", "AIO 后端", "-i", "threads", "threads", "native", "io_uring"),
            Toggle("native", "-n"), Toggle("forceShare", "-U"), Toggle("quiet", "-q")),

        Cmd("create", "创建", "创建新的磁盘镜像", true,
            Path("file", "输出镜像", true, true), Text("size", "虚拟大小", positional: true),
            Text("format", "格式", "-f", "qcow2"), Text("options", "格式选项", "-o", wide: true),
            Path("backing", "后备文件", false, false, "-b"), Text("backingFormat", "后备格式", "-B"),
            Toggle("backingUnsafe", "-u"), Toggle("quiet", "-q")),

        Cmd("convert", "转换", "转换一个或多个镜像", true,
            MultiPath("sources", "源镜像（以分号分隔）", true), Path("target", "目标镜像", true, true),
            Text("sourceFormat", "源格式", "-f"), Toggle("imageOpts", "--image-opts"), Cache("sourceCache", "源缓存", "-T", "writeback"),
            Text("snapshot", "源快照", "-l"), Toggle("bitmaps", "--bitmaps"), Toggle("skipBroken", "--skip-broken-bitmaps"),
            Toggle("salvage", "--salvage"), Text("targetFormat", "目标格式", "-O", "qcow2"),
            Toggle("targetImageOpts", "--target-image-opts"), Text("targetOptions", "目标格式选项", "-o", wide: true),
            Cache("targetCache", "目标缓存", "-t", "unsafe"), Path("backing", "后备文件", false, false, "-b"),
            Text("backingFormat", "后备格式", "-F"), Text("sparse", "稀疏阈值", "-S"),
            Toggle("noCreate", "-n"), Toggle("targetZero", "--target-is-zero"), Toggle("compress", "-c"),
            Toggle("forceShare", "-U"), Text("rate", "速率限制", "-r"), Text("parallel", "并行数", "-m", "8"),
            Toggle("copyRange", "-C"), Toggle("oobWrites", "-W"), Toggle("progress", "-p"), Toggle("quiet", "-q")),

        Cmd("dd", "转换", "按块复制镜像", true,
            AssignmentPath("input", "输入镜像", "if=", true, false), AssignmentPath("output", "输出镜像", "of=", true, true),
            Text("format", "输入格式", "-f"), Toggle("imageOpts", "--image-opts"), Text("outputFormat", "输出格式", "-O", "raw"),
            Assignment("blockSize", "块大小", "bs=", "512"), Assignment("count", "块数量", "count="), Toggle("forceShare", "-U")),

        Cmd("resize", "修改", "调整镜像虚拟容量", true,
            Path("file", "镜像文件", true, false), Text("size", "新大小或增量", positional: true, required: true),
            Text("format", "格式", "-f"), Toggle("imageOpts", "--image-opts"),
            Choice("preallocation", "预分配", "--preallocation", "off", "off", "metadata", "falloc", "full"),
            Toggle("shrink", "--shrink"), Toggle("quiet", "-q")),

        Cmd("rebase", "修改", "修改镜像后备文件", true,
            Path("file", "镜像文件", true, false), Text("format", "格式", "-f"), Toggle("imageOpts", "--image-opts"),
            Cache("cache", "缓存模式", "-t", "writeback"), Path("backing", "新后备文件（留空为无）", false, false, "-b"),
            Text("backingFormat", "后备格式", "-B"), Cache("backingCache", "后备缓存", "-T", "writeback"),
            Toggle("clearBacking", "移除后备文件"), Toggle("backingUnsafe", "-u"), Toggle("compress", "-c"), Toggle("forceShare", "-U"),
            Toggle("progress", "-p"), Toggle("quiet", "-q")),

        Cmd("commit", "修改", "提交到后备镜像", true,
            Path("file", "顶层镜像", true, false), Text("format", "格式", "-f"), Toggle("imageOpts", "--image-opts"),
            Cache("cache", "缓存模式", "-t", "writeback"), Path("base", "目标后备镜像", false, false, "-b"),
            Toggle("drop", "-d"), Text("rate", "速率限制", "-r"), Toggle("progress", "-p"), Toggle("quiet", "-q")),

        Cmd("amend", "修改", "修改格式专用选项", true,
            Path("file", "镜像文件", true, false), Text("options", "格式选项", "-o", required: true, wide: true),
            Text("format", "格式", "-f"), Toggle("imageOpts", "--image-opts"), Cache("cache", "缓存模式", "-t", "writeback"),
            Toggle("force", "--force"), Toggle("progress", "-p"), Toggle("quiet", "-q")),

        Cmd("snapshot", "快照", "管理内部快照", true,
            Path("file", "镜像文件", true, false), Action("action", "操作", "-l", "-c", "-a", "-d"),
            ConditionalText("snapshot", "快照名称", "action", "-l", true), Text("format", "格式", "-f"),
            Toggle("imageOpts", "--image-opts"), Toggle("forceShare", "-U"), Toggle("quiet", "-q")),

        Cmd("bitmap", "位图", "管理持久化位图", true,
            Path("file", "镜像文件", true, false), Action("action", "操作", "--add", "--remove", "--clear", "--enable", "--disable", "--merge"),
            ConditionalText("mergeSource", "源位图", "action", "--merge", false),
            Text("bitmap", "目标位图", positional: true, required: true), Text("format", "格式", "-f"),
            Toggle("imageOpts", "--image-opts"), Text("granularity", "粒度", "-g"),
            Path("sourceFile", "源镜像", false, false, "-b"), Text("sourceFormat", "源格式", "-F"))
    ];

    private static 磁盘命令 Cmd(string name, string category, string description, bool canWrite,
        params 磁盘字段[] fields)
    {
        var allFields = fields.Concat([
            Text("object", "对象定义（以分号分隔）", "--object", mode: 磁盘字段模式.MultiOptionValue, wide: true, advanced: true),
            Text("trace", "跟踪规则", "--trace", mode: 磁盘字段模式.GlobalOptionValue, wide: true, advanced: true),
            Text("extra", "额外参数", mode: 磁盘字段模式.RawArguments, wide: true, advanced: true)
        ]).Select(field => LocalizeField(name, field)).ToList();

        return new 磁盘命令
        {
            Name = name,
            DisplayName = 本地化.获取($"disk.command.{name}", name),
            Category = category,
            DisplayCategory = 本地化.获取(分类键(category), category),
            Description = 本地化.获取($"disk.description.{name}", description),
            Glyph = 命令图标(name),
            IconBrush = 命令画刷(name),
            CanWrite = canWrite,
            Fields = allFields
        };
    }

    private static 磁盘字段 LocalizeField(string command, 磁盘字段 field) => new()
    {
        Id = field.Id,
        Label = 本地化.获取($"disk.field.{command}.{field.Id}", 本地化.获取($"disk.field.{field.Id}", field.Label)),
        Mode = field.Mode,
        Argument = field.Argument,
        DefaultValue = field.DefaultValue,
        Choices = field.Choices,
        Required = field.Required,
        Wide = field.Wide,
        Advanced = field.Advanced,
        PathKind = field.PathKind,
        DependsOnId = field.DependsOnId,
        DependsOnValue = field.DependsOnValue,
        InvertDependency = field.InvertDependency
    };

    private static string 命令图标(string command) => command switch
    {
        "info" => "\uE946",
        "check" => "\uE9D9",
        "map" => "\uE81E",
        "measure" => "\uE9F9",
        "compare" => "\uE8AB",
        "bench" => "\uE9D2",
        "create" => "\uE710",
        "convert" => "\uE8AB",
        "dd" => "\uE8C8",
        "resize" => "\uE8B5",
        "rebase" => "\uE8A7",
        "commit" => "\uE74E",
        "amend" => "\uE70F",
        "snapshot" => "\uE7B8",
        "bitmap" => "\uE91B",
        _ => "\uE958"
    };

    private static SolidColorBrush 命令画刷(string command)
    {
        var color = command switch
        {
            "info" or "map" => ColorHelper.FromArgb(255, 48, 138, 210),
            "check" or "compare" => ColorHelper.FromArgb(255, 38, 166, 154),
            "measure" or "bench" => ColorHelper.FromArgb(255, 125, 104, 210),
            "create" => ColorHelper.FromArgb(255, 46, 160, 87),
            "convert" or "dd" => ColorHelper.FromArgb(255, 225, 139, 40),
            "resize" or "amend" => ColorHelper.FromArgb(255, 218, 90, 116),
            "rebase" or "commit" => ColorHelper.FromArgb(255, 195, 92, 190),
            "snapshot" => ColorHelper.FromArgb(255, 75, 113, 210),
            "bitmap" => ColorHelper.FromArgb(255, 80, 154, 128),
            _ => ColorHelper.FromArgb(255, 96, 110, 125)
        };
        return new SolidColorBrush(color);
    }

    private static string 分类键(string category) => category switch
    {
        "检查" => "disk.category.inspect",
        "创建" => "disk.category.create",
        "转换" => "disk.category.convert",
        "修改" => "disk.category.modify",
        "快照" => "disk.category.snapshot",
        "位图" => "disk.category.bitmap",
        _ => category
    };

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
