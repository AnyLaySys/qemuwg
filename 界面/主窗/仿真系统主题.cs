using Microsoft.UI;
using Windows.UI;

namespace QemuWG.界面;

public readonly record struct 仿真系统配色(Color 背景色, Color 前景色);

public static class 仿真系统主题
{
    private static readonly Color 浅色文字 = Colors.White;
    private static readonly Color 深色文字 = Color.FromArgb(255, 20, 30, 24);

    public static 仿真系统配色 获取(string 系统标识) => 系统标识 switch
    {
        "ubuntu" => 配色("#E95420", 浅色文字),
        "android" => 配色("#3DDC84", 深色文字),
        "windows" => 配色("#0078D4", 浅色文字),
        "macos" => 配色("#55565A", 浅色文字),
        "debian" => 配色("#A80030", 浅色文字),
        "fedora" => 配色("#294172", 浅色文字),
        "arch" => 配色("#1793D1", 浅色文字),
        "kali" => 配色("#367BF0", 浅色文字),
        "mint" => 配色("#86BE43", 深色文字),
        "suse" => 配色("#73BA25", 深色文字),
        "redhat" => 配色("#EE0000", 浅色文字),
        "centos" => 配色("#932279", 浅色文字),
        "rocky" => 配色("#10B981", 深色文字),
        "alma" => 配色("#0E9A7B", 浅色文字),
        "chromeos" => 配色("#1A73E8", 浅色文字),
        "freebsd" => 配色("#AB2B28", 浅色文字),
        "openbsd" => 配色("#F2CA30", 深色文字),
        "netbsd" => 配色("#F05A28", 浅色文字),
        "haiku" => 配色("#F0D400", 深色文字),
        "solaris" => 配色("#F47820", 浅色文字),
        "linux" => 配色("#FCC624", 深色文字),
        _ => 配色("#64748B", 浅色文字)
    };

    private static 仿真系统配色 配色(string 十六进制背景色, Color 前景色) =>
        new(解析颜色(十六进制背景色), 前景色);

    private static Color 解析颜色(string 十六进制)
    {
        var 数值 = Convert.ToUInt32(十六进制[1..], 16);
        return Color.FromArgb(255, (byte)(数值 >> 16), (byte)(数值 >> 8), (byte)数值);
    }
}
