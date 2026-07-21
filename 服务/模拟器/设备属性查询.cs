using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU服务
{
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<QEMU设备属性>>> devicePropertyCache = new();

    public Task<IReadOnlyList<QEMU设备属性>> 获取设备属性(QEMU架构 arch, string device) =>
        devicePropertyCache.GetOrAdd($"{arch.Id}:{device}", _ => 查询设备属性(arch, device));

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

    private static IReadOnlyList<string> 解析设备属性候选(string description)
    {
        var defaultIndex = description.IndexOf("(default:", StringComparison.OrdinalIgnoreCase);
        var choicesText = (defaultIndex >= 0 ? description[..defaultIndex] : description).Trim();
        if (choicesText.Length == 0 || !choicesText.Contains('/')) return [];
        return choicesText.Split('/', StringSplitOptions.TrimEntries).ToList();
    }

    [GeneratedRegex("^\\s*([^=\\s]+)=<([^>]+)>\\s*(?:-\\s*(.*))?$")]
    private static partial Regex 设备属性正则();

    [GeneratedRegex("\\(default:\\s*([^\\)]+)\\)", RegexOptions.IgnoreCase)]
    private static partial Regex 默认值正则();
}
