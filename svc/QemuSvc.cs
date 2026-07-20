using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using QemuWG.data;

namespace QemuWG.svc;

public sealed partial class QemuSvc
{
    private readonly ConcurrentDictionary<string, Task<QemuCaps>> capabilityCache = new();
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<QemuDevicePropDef>>> devicePropertyCache = new();

    public Task<QemuInstall> DetectAsync() => Task.Run(async () =>
    {
        var roots = new List<string>();
        AddCandidateRoot(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "qemu"));
        AddCandidateRoot(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "qemu"));

        foreach (var path in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddCandidateRoot(roots, path);
        }

        var root = roots.FirstOrDefault(candidate => File.Exists(Path.Combine(candidate, "qemu-system-x86_64.exe")))
                   ?? roots.FirstOrDefault(candidate => Directory.EnumerateFiles(candidate, "qemu-system-*.exe").Any());
        if (root is null) return new QemuInstall();

        var executables = Directory.EnumerateFiles(root, "qemu-system-*.exe")
            .Select(path => new
            {
                Path = path,
                Id = Path.GetFileNameWithoutExtension(path)["qemu-system-".Length..]
            })
            .ToList();

        var baseIds = executables.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var architectures = executables
            .Where(item => !(item.Id.EndsWith("w", StringComparison.OrdinalIgnoreCase) && baseIds.Contains(item.Id[..^1])))
            .Select(item => new QemuArch(item.Id, item.Id, item.Path))
            .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var primary = architectures.FirstOrDefault(item => item.Id == "x86_64") ?? architectures.First();
        var versionResult = await ProcessRunner.RunAsync(primary.ExecutablePath, ["--version"]);
        var version = versionResult.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                      ?? "QEMU";
        version = version.Replace("QEMU emulator version ", "QEMU ", StringComparison.OrdinalIgnoreCase);

        return new QemuInstall
        {
            IsAvailable = true,
            RootDir = root,
            ImgToolPath = Path.Combine(root, "qemu-img.exe"),
            Version = version,
            Archs = architectures
        };
    });

    public Task<QemuCaps> GetCapabilitiesAsync(QemuArch arch) =>
        capabilityCache.GetOrAdd(arch.Id, _ => QueryCapabilitiesAsync(arch));

    public Task<IReadOnlyList<QemuDevicePropDef>> GetDevicePropertiesAsync(QemuArch arch, string device) =>
        devicePropertyCache.GetOrAdd($"{arch.Id}:{device}", _ => QueryDevicePropertiesAsync(arch, device));

    public string? FindFirmware(QemuInstall install, string arch, bool variables)
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

    private static async Task<QemuCaps> QueryCapabilitiesAsync(QemuArch arch)
    {
        var machineTask = ProcessRunner.RunAsync(arch.ExecutablePath, ["-machine", "help"]);
        var cpuTask = ProcessRunner.RunAsync(arch.ExecutablePath, ["-cpu", "help"]);
        var accelTask = ProcessRunner.RunAsync(arch.ExecutablePath, ["-accel", "help"]);
        var displayTask = ProcessRunner.RunAsync(arch.ExecutablePath, ["-display", "help"]);
        var deviceTask = ProcessRunner.RunAsync(arch.ExecutablePath, ["-device", "help"]);
        var helpTask = ProcessRunner.RunAsync(arch.ExecutablePath, ["-help"]);
        await Task.WhenAll(machineTask, cpuTask, accelTask, displayTask, deviceTask, helpTask);

        var devices = deviceTask.Result.Output;
        return new QemuCaps
        {
            Machines = ParseFirstColumn(machineTask.Result.Output, ["Supported machines", "none"]),
            CpuModels = ParseCpuModels(cpuTask.Result.Output),
            Accelerators = ParseFirstColumn(accelTask.Result.Output, ["Accelerators supported"]),
            DisplayBackends = ParseDisplayBackends(displayTask.Result.Output),
            VideoDevices = ParseDevices(devices, "display"),
            NetworkDevices = ParseDevices(devices, "network"),
            AudioDevices = ParseDevices(devices, "sound"),
            AllDevices = ParseAllDevices(devices),
            CmdOptions = ParseCmdOptions(helpTask.Result.Output)
        };
    }

    private static async Task<IReadOnlyList<QemuDevicePropDef>> QueryDevicePropertiesAsync(QemuArch arch, string device)
    {
        var result = await ProcessRunner.RunAsync(arch.ExecutablePath, ["-device", $"{device},help"]);
        var properties = new List<QemuDevicePropDef>();
        foreach (var line in result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = DevicePropertyRegex().Match(line);
            if (!match.Success) continue;
            var type = match.Groups[2].Value;
            var description = match.Groups[3].Value.Trim();
            var defaultMatch = DefaultValueRegex().Match(description);
            properties.Add(new QemuDevicePropDef
            {
                Name = match.Groups[1].Value,
                Type = type,
                Description = description,
                DefaultValue = defaultMatch.Success ? defaultMatch.Groups[1].Value : string.Empty,
                Choices = type.Equals("bool", StringComparison.OrdinalIgnoreCase) ? ["on", "off"]
                    : type.Equals("OnOffAuto", StringComparison.OrdinalIgnoreCase) ? ["auto", "on", "off"] : []
            });
        }
        return properties;
    }

    private static IReadOnlyList<string> ParseAllDevices(string output) => output
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(line => DeviceNameRegex().Match(line.Trim()))
        .Where(match => match.Success)
        .Select(match => match.Groups[1].Value)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static IReadOnlyList<QemuCmdOptionDef> ParseCmdOptions(string output)
    {
        var result = new Dictionary<string, QemuCmdOptionDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = CommandLineOptionRegex().Match(line);
            if (!match.Success) continue;
            var name = match.Groups[1].Value;
            if (name.Length == 0 || name == "-") continue;
            result.TryAdd(name, new QemuCmdOptionDef
            {
                Name = name,
                Syntax = match.Groups[2].Value.Trim()
            });
        }
        return result.Values.OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> ParseFirstColumn(string output, IReadOnlyList<string> ignored)
    {
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !ignored.Any(value => line.StartsWith(value, StringComparison.OrdinalIgnoreCase)))
            .Select(line => WhitespaceRegex().Split(line)[0])
            .Where(value => value.Length > 0 && value.All(ch => !char.IsWhiteSpace(ch)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ParseCpuModels(string output)
    {
        var models = new List<string>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Available", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("Recognized", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = WhitespaceRegex().Split(trimmed);
            var candidate = parts.Length > 1 && parts[0].EndsWith("CPU", StringComparison.OrdinalIgnoreCase) ? parts[1] : parts[0];
            if (!string.IsNullOrWhiteSpace(candidate) && !candidate.Contains(':')) models.Add(candidate);
        }
        return models.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> ParseDisplayBackends(string output) => output
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim().TrimEnd(','))
        .Where(line => BackendNameRegex().IsMatch(line))
        .Where(line => !string.Equals(line, "sdl", StringComparison.OrdinalIgnoreCase))
        .OrderBy(line => string.Equals(line, "gtk", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
        .ThenBy(line => line, StringComparer.OrdinalIgnoreCase)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static IReadOnlyList<string> ParseDevices(string output, string category)
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
            var match = DeviceNameRegex().Match(trimmed);
            if (match.Success) devices.Add(match.Groups[1].Value);
        }
        return devices.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddCandidateRoot(ICollection<string> roots, string path)
    {
        if (!Directory.Exists(path) || roots.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        roots.Add(path);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("name \\\"([^\\\"]+)\\\"")]
    private static partial Regex DeviceNameRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*$", RegexOptions.IgnoreCase)]
    private static partial Regex BackendNameRegex();

    [GeneratedRegex("^\\s*-([A-Za-z0-9][A-Za-z0-9_-]*)(?:\\s+(.*))?$")]
    private static partial Regex CommandLineOptionRegex();

    [GeneratedRegex("^\\s*([^=\\s]+)=<([^>]+)>\\s*(?:-\\s*(.*))?$")]
    private static partial Regex DevicePropertyRegex();

    [GeneratedRegex("\\(default:\\s*([^\\)]+)\\)", RegexOptions.IgnoreCase)]
    private static partial Regex DefaultValueRegex();
}




