namespace QemuWG.数据;

public static class 仿真系统标识
{
    public const string 自动 = "auto";
    public const string 其他 = "other";

    public static string 更新(string? 当前标识, string? 名称, string? 镜像路径)
    {
        var 推断结果 = 推断(名称, 镜像路径);
        if (!string.Equals(推断结果, 其他, StringComparison.Ordinal)) return 推断结果;

        var 规范标识 = 规范化(当前标识);
        return string.Equals(规范标识, 自动, StringComparison.Ordinal) ? 其他 : 规范标识;
    }

    public static string 解析(string? 当前标识, string? 名称, string? 镜像路径)
    {
        var 规范标识 = 规范化(当前标识);
        return string.Equals(规范标识, 自动, StringComparison.Ordinal)
            ? 推断(名称, 镜像路径)
            : 规范标识;
    }

    public static string 推断(string? 名称, string? 镜像路径)
    {
        var 镜像名称 = string.IsNullOrWhiteSpace(镜像路径) ? string.Empty : Path.GetFileNameWithoutExtension(镜像路径);
        var 文本 = $"{名称} {镜像名称}".ToLowerInvariant();

        if (包含(文本, "android", "bliss os", "primeos", "phoenix os")) return "android";
        if (包含(文本, "ubuntu", "kubuntu", "xubuntu", "lubuntu")) return "ubuntu";
        if (包含(文本, "debian")) return "debian";
        if (包含(文本, "fedora")) return "fedora";
        if (包含词(文本, "arch") || 包含(文本, "archlinux", "arch linux")) return "arch";
        if (包含(文本, "kali")) return "kali";
        if (包含(文本, "linux mint", "linuxmint") || 包含词(文本, "mint")) return "mint";
        if (包含(文本, "opensuse", "open suse") || 包含词(文本, "suse")) return "suse";
        if (包含(文本, "red hat", "redhat") || 包含词(文本, "rhel")) return "redhat";
        if (包含(文本, "centos")) return "centos";
        if (包含(文本, "rocky linux", "rockylinux")) return "rocky";
        if (包含(文本, "alma linux", "almalinux")) return "alma";
        if (包含(文本, "macos", "mac os", "osx", "os x", "darwin")) return "macos";
        if (包含(文本, "windows", "win11", "win 11", "win10", "win 10", "win8", "win 8", "win7", "win 7")) return "windows";
        if (包含(文本, "chromeos", "chrome os", "chromiumos", "chromium os")) return "chromeos";
        if (包含(文本, "freebsd")) return "freebsd";
        if (包含(文本, "openbsd")) return "openbsd";
        if (包含(文本, "netbsd")) return "netbsd";
        if (包含(文本, "haiku")) return "haiku";
        if (包含(文本, "solaris", "illumos")) return "solaris";
        if (包含(文本, "linux")) return "linux";
        return 其他;
    }

    private static string 规范化(string? 标识) => string.IsNullOrWhiteSpace(标识)
        ? 自动
        : 标识.Trim().ToLowerInvariant();

    private static bool 包含(string 文本, params string[] 片段) => 片段.Any(文本.Contains);

    private static bool 包含词(string 文本, string 词)
    {
        var 起点 = 0;
        while ((起点 = 文本.IndexOf(词, 起点, StringComparison.Ordinal)) >= 0)
        {
            var 左边界 = 起点 == 0 || !char.IsLetterOrDigit(文本[起点 - 1]);
            var 右侧位置 = 起点 + 词.Length;
            var 右边界 = 右侧位置 == 文本.Length || !char.IsLetterOrDigit(文本[右侧位置]);
            if (左边界 && 右边界) return true;
            起点 = 右侧位置;
        }
        return false;
    }
}
