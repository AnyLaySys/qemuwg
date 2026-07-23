using QemuWG.数据;

namespace QemuWG.服务;

public sealed class 系统磁盘检查
{
    private static string T(string key, string fallback) => 语言服务.当前.获取(key, fallback);
    private readonly QEMU镜像 imageService = new();

    public async Task<(操作结果 结果, string 路径, 磁盘镜像信息? 信息)> 检查(
        QEMU安装 install,
        string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (操作结果.Fail(T("vmEditor.validation.diskPath", "请选择已有系统磁盘。")), string.Empty, null);

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path.Trim());
        }
        catch (Exception exception)
        {
            return (操作结果.Fail(T("repo.diskInspectFailed", "无法读取所选系统磁盘的格式与容量"), exception.Message), string.Empty, null);
        }

        if (!File.Exists(fullPath))
            return (操作结果.Fail(T("repo.existingDiskMissing", "找不到所选系统磁盘"), fullPath), fullPath, null);
        if (!File.Exists(install.ImgToolPath))
            return (操作结果.Fail(T("repo.imgMissing", "未找到 qemu-img")), fullPath, null);

        try
        {
            var info = await imageService.获取信息(install, fullPath);
            if (info is null || string.IsNullOrWhiteSpace(info.Format)
                             || string.Equals(info.Format, "unknown", StringComparison.OrdinalIgnoreCase))
                return (操作结果.Fail(T("repo.diskInspectFailed", "无法读取所选系统磁盘的格式与容量"), fullPath), fullPath, null);
            return (操作结果.Ok(string.Empty), fullPath, info);
        }
        catch (Exception exception)
        {
            return (操作结果.Fail(T("repo.diskInspectFailed", "无法读取所选系统磁盘的格式与容量"), exception.Message), fullPath, null);
        }
    }

    public static int 转换容量(long bytes)
    {
        if (bytes <= 0) return 1;
        var gibibytes = (long)Math.Ceiling(bytes / 1073741824d);
        return (int)Math.Clamp(gibibytes, 1, int.MaxValue);
    }
}
