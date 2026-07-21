using QemuWG.数据;

namespace QemuWG.服务;

public static partial class QEMU镜像命令
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

}
