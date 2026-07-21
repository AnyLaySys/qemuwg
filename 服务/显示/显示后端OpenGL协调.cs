using QemuWG.数据;

namespace QemuWG.服务;

internal static class QEMU显示要求
{
    private static readonly HashSet<string> 支持OpenGL选项的后端 = new(StringComparer.OrdinalIgnoreCase)
    {
        "dbus", "gtk", "sdl", "spice-app"
    };

    public static bool 需要OpenGL(虚拟机配置 vm, IReadOnlyList<string> extraArguments)
    {
        if (是OpenGL设备(vm.VideoDevice)) return true;
        if (vm.Devices.Any(device => 是OpenGL设备(device.Model))) return true;
        if (vm.QemuOpts.Any(option => 是设备选项(option.Name) && 是OpenGL设备(option.Value))) return true;

        for (var index = 0; index + 1 < extraArguments.Count; index++)
        {
            if (是设备选项(extraArguments[index]) && 是OpenGL设备(extraArguments[index + 1]))
                return true;
        }
        return false;
    }

    public static IEnumerable<string> 枚举显示后端(虚拟机配置 vm, IReadOnlyList<string> extraArguments)
    {
        yield return string.IsNullOrWhiteSpace(vm.DisplayBackend) ? "dbus" : vm.DisplayBackend.Trim();
        foreach (var option in vm.QemuOpts)
        {
            if (是显示选项(option.Name) && !string.IsNullOrWhiteSpace(option.Value))
                yield return option.Value.Trim();
        }
        for (var index = 0; index + 1 < extraArguments.Count; index++)
        {
            if (是显示选项(extraArguments[index])) yield return extraArguments[index + 1];
        }
    }

    public static IReadOnlyList<string> 启用额外显示OpenGL(IReadOnlyList<string> arguments)
    {
        var result = arguments.ToArray();
        for (var index = 0; index + 1 < result.Length; index++)
        {
            if (是显示选项(result[index])) result[index + 1] = 启用OpenGL(result[index + 1]);
        }
        return result;
    }

    public static bool 支持OpenGL(string displayBackend)
    {
        var backend = 获取后端名称(displayBackend);
        return 支持OpenGL选项的后端.Contains(backend)
               || string.Equals(backend, "egl-headless", StringComparison.OrdinalIgnoreCase);
    }

    public static bool 显式关闭OpenGL(string displayBackend) =>
        获取后端选项(displayBackend)
            .Any(option => string.Equals(option, "gl=off", StringComparison.OrdinalIgnoreCase));

    public static string 启用OpenGL(string displayBackend)
    {
        if (string.Equals(获取后端名称(displayBackend), "egl-headless", StringComparison.OrdinalIgnoreCase))
            return displayBackend;
        if (获取后端选项(displayBackend).Any(option => option.StartsWith("gl=", StringComparison.OrdinalIgnoreCase)))
            return displayBackend;
        return displayBackend + ",gl=on";
    }

    public static string 获取后端名称(string displayBackend)
    {
        if (string.IsNullOrWhiteSpace(displayBackend)) return "dbus";
        var comma = displayBackend.IndexOf(',');
        return (comma < 0 ? displayBackend : displayBackend[..comma]).Trim();
    }

    private static IEnumerable<string> 获取后端选项(string displayBackend)
    {
        if (string.IsNullOrWhiteSpace(displayBackend)) yield break;
        var parts = displayBackend.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (var index = 1; index < parts.Length; index++) yield return parts[index];
    }

    public static bool 是显示选项(string name) =>
        string.Equals(name.Trim(), "-display", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name.Trim(), "--display", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name.Trim(), "display", StringComparison.OrdinalIgnoreCase);

    private static bool 是设备选项(string name) =>
        string.Equals(name.Trim(), "-device", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name.Trim(), "--device", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name.Trim(), "device", StringComparison.OrdinalIgnoreCase);

    private static bool 是OpenGL设备(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var comma = value.IndexOf(',');
        var model = (comma < 0 ? value : value[..comma]).Trim();
        return model.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Any(part => string.Equals(part, "gl", StringComparison.OrdinalIgnoreCase));
    }
}
