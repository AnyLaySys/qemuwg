using System.Reflection;
using System.Text.Json;

namespace QemuWG.服务;

public sealed class 语言服务
{
    private static readonly IReadOnlyDictionary<string, string> 中文文本 = 加载("zh-CN");
    private static readonly IReadOnlyDictionary<string, string> 英文文本 = 加载("en-US");
    private readonly IReadOnlyDictionary<string, string> 文本表;

    static 语言服务()
    {
        验证语言表(中文文本, 英文文本);
    }

    public 语言服务() : this("zh-CN")
    {
    }

    private 语言服务(string 语言代码)
    {
        语言 = 语言代码;
        文本表 = 语言代码 switch
        {
            "zh-CN" => 中文文本,
            "en-US" => 英文文本,
            _ => 加载(语言代码)
        };
    }

    public static 语言服务 当前 { get; } = new("zh-CN");

    public string 语言 { get; }

    public string this[string 键] =>
        文本表.TryGetValue(键, out var 值) && !string.IsNullOrWhiteSpace(值)
            ? 值
            : throw new KeyNotFoundException($"未找到翻译键“{键}”。");

    public string 获取(string 键, string 回退文本)
    {
        return 文本表.TryGetValue(键, out var 值) && !string.IsNullOrWhiteSpace(值) ? 值 : 回退文本;
    }

    private static IReadOnlyDictionary<string, string> 加载(string 语言代码)
    {
        var 程序集 = Assembly.GetExecutingAssembly();
        var 后缀 = $".语言.{语言代码}.json";
        var 资源名 = 程序集.GetManifestResourceNames()
            .FirstOrDefault(名称 => 名称.EndsWith(后缀, StringComparison.OrdinalIgnoreCase));
        if (资源名 is null)
        {
            var 可用资源 = string.Join(", ", 程序集.GetManifestResourceNames());
            throw new InvalidOperationException($"找不到语言资源“{后缀}”。可用资源：{可用资源}");
        }

        using var 流 = 程序集.GetManifestResourceStream(资源名);
        if (流 is null) throw new InvalidOperationException($"无法读取语言资源“{资源名}”。");

        using var 文档 = JsonDocument.Parse(流);
        if (文档.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"语言资源“{资源名}”的根节点必须是 JSON 对象。");

        var 文本表 = new Dictionary<string, string>(StringComparer.Ordinal);
        var 忽略大小写键 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var 项 in 文档.RootElement.EnumerateObject())
        {
            if (项.Value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"语言资源“{资源名}”中的“{项.Name}”必须是字符串。");
            if (string.IsNullOrWhiteSpace(项.Name))
                throw new InvalidDataException($"语言资源“{资源名}”包含空键。");

            var 值 = 项.Value.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(值))
                throw new InvalidDataException($"语言资源“{资源名}”中的“{项.Name}”不能为空。");
            if (!文本表.TryAdd(项.Name, 值))
                throw new InvalidDataException($"语言资源“{资源名}”包含重复键“{项.Name}”。");
            if (!忽略大小写键.Add(项.Name))
                throw new InvalidDataException($"语言资源“{资源名}”包含仅大小写不同的键“{项.Name}”。");
        }

        return 文本表;
    }

    private static void 验证语言表(
        IReadOnlyDictionary<string, string> 中文,
        IReadOnlyDictionary<string, string> 英文)
    {
        var 缺少英文 = 中文.Keys.Except(英文.Keys, StringComparer.Ordinal).OrderBy(键 => 键).ToArray();
        var 缺少中文 = 英文.Keys.Except(中文.Keys, StringComparer.Ordinal).OrderBy(键 => 键).ToArray();
        if (缺少英文.Length > 0 || 缺少中文.Length > 0)
        {
            throw new InvalidDataException(
                $"中英文语言键不一致。缺少英文：{string.Join(", ", 缺少英文)}；缺少中文：{string.Join(", ", 缺少中文)}");
        }

        foreach (var 键 in 中文.Keys)
        {
            var 中文占位符 = 获取占位符(中文[键]);
            var 英文占位符 = 获取占位符(英文[键]);
            if (!中文占位符.SequenceEqual(英文占位符))
            {
                throw new InvalidDataException(
                    $"语言键“{键}”的格式占位符不一致：中文 {{{string.Join(", ", 中文占位符)}}}，英文 {{{string.Join(", ", 英文占位符)}}}。");
            }
        }
    }

    private static IReadOnlyList<int> 获取占位符(string 文本) =>
        System.Text.RegularExpressions.Regex.Matches(文本, @"(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})")
            .Select(匹配 => int.Parse(匹配.Groups[1].Value))
            .Distinct()
            .OrderBy(编号 => 编号)
            .ToArray();
}
