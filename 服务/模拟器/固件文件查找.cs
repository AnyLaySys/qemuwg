using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU服务
{
    public string? 查找固件(QEMU安装 install, string arch, bool variables)
    {
        var share = Path.Combine(install.RootDir, "share");
        var target = arch.EndsWith("w", StringComparison.OrdinalIgnoreCase)
                     && install.Archs.Any(item => string.Equals(item.Id, arch[..^1], StringComparison.OrdinalIgnoreCase))
            ? arch[..^1]
            : arch;
        var name = (target, variables) switch
        {
            ("x86_64", false) => "edk2-x86_64-code.fd",
            ("x86_64", true) => "edk2-i386-vars.fd",
            ("i386", false) => "edk2-i386-code.fd",
            ("i386", true) => "edk2-i386-vars.fd",
            ("aarch64", false) => "edk2-aarch64-code.fd",
            ("aarch64", true) => "edk2-arm-vars.fd",
            ("arm", false) => "edk2-arm-code.fd",
            ("arm", true) => "edk2-arm-vars.fd",
            ("riscv64", false) => "edk2-riscv-code.fd",
            ("riscv64", true) => "edk2-riscv-vars.fd",
            ("loongarch64", false) => "edk2-loongarch64-code.fd",
            ("loongarch64", true) => "edk2-loongarch64-vars.fd",
            _ => string.Empty
        };
        if (string.IsNullOrEmpty(name)) return null;
        var path = Path.Combine(share, name);
        return File.Exists(path) ? path : null;
    }
}
