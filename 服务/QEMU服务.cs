using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU服务
{
    private readonly ConcurrentDictionary<string, Task<QEMU能力>> capabilityCache = new();
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<QEMU设备属性>>> devicePropertyCache = new();

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

    public Task<QEMU能力> 获取能力(QEMU架构 arch) =>
        capabilityCache.GetOrAdd(arch.Id, _ => 查询能力(arch));

    public Task<IReadOnlyList<QEMU设备属性>> 获取设备属性(QEMU架构 arch, string device) =>
        devicePropertyCache.GetOrAdd($"{arch.Id}:{device}", _ => 查询设备属性(arch, device));

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

    private static async Task<QEMU能力> 查询能力(QEMU架构 arch)
    {
        var machineTask = 进程.运行(arch.ExecutablePath, ["-machine", "help"]);
        var cpuTask = 进程.运行(arch.ExecutablePath, ["-cpu", "help"]);
        var accelTask = 进程.运行(arch.ExecutablePath, ["-accel", "help"]);
        var displayTask = 进程.运行(arch.ExecutablePath, ["-display", "help"]);
        var networkBackendTask = 进程.运行(arch.ExecutablePath, ["-netdev", "help"]);
        var audioBackendTask = 进程.运行(arch.ExecutablePath, ["-audiodev", "help"]);
        var deviceTask = 进程.运行(arch.ExecutablePath, ["-device", "help"]);
        var helpTask = 进程.运行(arch.ExecutablePath, ["-help"]);
        await Task.WhenAll(machineTask, cpuTask, accelTask, displayTask, networkBackendTask, audioBackendTask, deviceTask, helpTask);

        var devices = deviceTask.Result.输出;
        return new QEMU能力
        {
            Machines = 解析首列块(machineTask.Result.输出, "Supported machines"),
            CpuModels = 解析首列块(cpuTask.Result.输出, "Available CPUs"),
            Accelerators = 解析首列块(accelTask.Result.输出, "Accelerators supported"),
            DisplayBackends = 解析首列块(displayTask.Result.输出, "Available display backend types"),
            VideoDevices = 解析设备(devices, "display"),
            NetworkBackends = 解析首列块(networkBackendTask.Result.输出, "Available netdev backend types"),
            NetworkDevices = 解析设备(devices, "network"),
            AudioBackends = 解析首列块(audioBackendTask.Result.输出, "Available audio drivers"),
            AudioDevices = 解析设备(devices, "sound"),
            AllDevices = 解析全部设备(devices),
            CmdOptions = 解析命令选项(helpTask.Result.输出)
        };
    }

    private static async Task<IReadOnlyList<QEMU设备属性>> 查询设备属性(QEMU架构 arch, string device)
    {
        var result = await 进程.运行(arch.ExecutablePath, ["-device", $"{device},help"]);
        var properties = new List<QEMU设备属性>();
        foreach (var line in result.输出.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = 设备属性正则().Match(line);
            if (!match.Success) continue;
            var type = match.Groups[2].Value;
            var description = match.Groups[3].Value.Trim();
            var defaultMatch = 默认值正则().Match(description);
            properties.Add(new QEMU设备属性
            {
                Name = match.Groups[1].Value,
                Type = type,
                Description = description,
                DefaultValue = defaultMatch.Success ? defaultMatch.Groups[1].Value : string.Empty,
                Choices = 解析设备属性候选(description)
            });
        }
        return properties;
    }

    private static IReadOnlyList<string> 解析全部设备(string output) => output
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(line => 设备名正则().Match(line.Trim()))
        .Where(match => match.Success)
        .Select(match => match.Groups[1].Value)
        .ToList();

    private static IReadOnlyList<QEMU命令选项> 解析命令选项(string output)
    {
        var result = new List<QEMU命令选项>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = 命令行选项正则().Match(line);
            if (!match.Success) continue;
            var name = match.Groups[1].Value;
            if (name.Length == 0 || name == "-") continue;
            result.Add(new QEMU命令选项
            {
                Name = name,
                Syntax = match.Groups[2].Value.Trim()
            });
        }
        return result;
    }

    private static IReadOnlyList<string> 解析首列块(string output, string heading)
    {
        var values = new List<string>();
        var collecting = false;
        foreach (var line in output.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var trimmed = line.Trim();
            if (!collecting)
            {
                if (trimmed.StartsWith(heading, StringComparison.OrdinalIgnoreCase)) collecting = true;
                continue;
            }

            if (trimmed.Length == 0)
            {
                if (values.Count > 0) break;
                continue;
            }

            values.Add(空白正则().Split(trimmed)[0]);
        }
        return values;
    }

    private static IReadOnlyList<string> 解析设备属性候选(string description)
    {
        var defaultIndex = description.IndexOf("(default:", StringComparison.OrdinalIgnoreCase);
        var choicesText = (defaultIndex >= 0 ? description[..defaultIndex] : description).Trim();
        if (choicesText.Length == 0 || !choicesText.Contains('/')) return [];
        return choicesText.Split('/', StringSplitOptions.TrimEntries).ToList();
    }

    private static IReadOnlyList<string> 解析设备(string output, string category)
    {
        var currentCategory = string.Empty;
        var devices = new List<string>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.EndsWith(" devices:", StringComparison.OrdinalIgnoreCase))
            {
                currentCategory = trimmed[..^" devices:".Length].ToLowerInvariant();
                continue;
            }
            if (!trimmed.StartsWith("name ", StringComparison.OrdinalIgnoreCase) && !trimmed.StartsWith('"')) continue;

            if (currentCategory != category) continue;
            var match = 设备名正则().Match(trimmed);
            if (match.Success) devices.Add(match.Groups[1].Value);
        }
        return devices;
    }

    private static void 添加候选根目录(ICollection<string> roots, string path)
    {
        if (!Directory.Exists(path) || roots.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        roots.Add(path);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex 空白正则();

    [GeneratedRegex("name \\\"([^\\\"]+)\\\"")]
    private static partial Regex 设备名正则();

    [GeneratedRegex("^\\s*(--?\\S+)(?:\\s+(.*))?$")]
    private static partial Regex 命令行选项正则();

    [GeneratedRegex("^\\s*([^=\\s]+)=<([^>]+)>\\s*(?:-\\s*(.*))?$")]
    private static partial Regex 设备属性正则();

    [GeneratedRegex("\\(default:\\s*([^\\)]+)\\)", RegexOptions.IgnoreCase)]
    private static partial Regex 默认值正则();
}
