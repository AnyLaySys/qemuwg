using System.Reflection;
using System.Text.Json;

namespace QemuWG.svc;

public sealed class LocaleSvc
{
    private readonly IReadOnlyDictionary<string, string> strings;

    public LocaleSvc() : this("zh-CN")
    {
    }

    private LocaleSvc(string language)
    {
        Language = language;
        strings = Load(language);
    }

    public static LocaleSvc Current { get; } = new("zh-CN");

    public string Language { get; }

    public string this[string key] => Get(key, key);

    public string Get(string key, string fallback)
    {
        return strings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
    }

    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var suffix = $"lng.{language}.json";
        var resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (resource is null) return new Dictionary<string, string>();

        using var stream = assembly.GetManifestResourceStream(resource);
        if (stream is null) return new Dictionary<string, string>();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
               ?? new Dictionary<string, string>();
    }
}

