using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU服务
{
    public Task<QEMU安装> 检测() => Task.Run(async () =>
    {
        var roots = new List<string>();
        添加候选根目录(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "qemu"));
        添加候选根目录(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "qemu"));

        foreach (var path in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            添加候选根目录(roots, path);
        }

        var root = roots.FirstOrDefault(candidate => File.Exists(Path.Combine(candidate, "qemu-system-x86_64.exe")))
                   ?? roots.FirstOrDefault(candidate => Directory.EnumerateFiles(candidate, "qemu-system-*.exe").Any());
        if (root is null) return new QEMU安装();

        var executables = Directory.EnumerateFiles(root, "qemu-system-*.exe")
            .Select(path => new
            {
                Path = path,
                Id = Path.GetFileNameWithoutExtension(path)["qemu-system-".Length..]
            })
            .ToList();

        var architectures = executables
            .Select(item => new QEMU架构(item.Id, item.Id, item.Path))
            .ToList();

        var primary = architectures.FirstOrDefault(item => item.Id == "x86_64") ?? architectures.First();
        var versionResult = await 进程.运行(primary.ExecutablePath, ["--version"]);
        var version = versionResult.输出.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                      ?? "QEMU";
        version = version.Replace("QEMU emulator version ", "QEMU ", StringComparison.OrdinalIgnoreCase);

        return new QEMU安装
        {
            IsAvailable = true,
            RootDir = root,
            ImgToolPath = Path.Combine(root, "qemu-img.exe"),
            Version = version,
            Archs = architectures
        };
    });

    private static void 添加候选根目录(ICollection<string> roots, string path)
    {
        if (!Directory.Exists(path) || roots.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        roots.Add(path);
    }
}
