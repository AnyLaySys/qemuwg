using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU服务
{
    private readonly ConcurrentDictionary<string, Task<QEMU能力>> capabilityCache = new();

    public Task<QEMU能力> 获取能力(QEMU架构 arch) =>
        capabilityCache.GetOrAdd(arch.Id, _ => 查询能力(arch));

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
            InputDevices = 解析设备(devices, "input"),
            AllDevices = 解析全部设备(devices),
            CmdOptions = 解析命令选项(helpTask.Result.输出)
        };
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

    [GeneratedRegex(@"\s+")]
    private static partial Regex 空白正则();

    [GeneratedRegex("name \\\"([^\\\"]+)\\\"")]
    private static partial Regex 设备名正则();

    [GeneratedRegex("^\\s*(--?\\S+)(?:\\s+(.*))?$")]
    private static partial Regex 命令行选项正则();
}
