using System.Text.Json;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed class QEMU镜像
{
    public 磁盘命令参数 BuildArgs(
        磁盘命令 command,
        IReadOnlyDictionary<string, string> values)
    {
        var global = new List<string>();
        var options = new List<string>();
        var positional = new List<string>();
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        HandleCompoundActions(command, values, options, consumed);

        foreach (var field in command.Fields)
        {
            if (consumed.Contains(field.Id) || !DependencyMatches(field, values)) continue;
            values.TryGetValue(field.Id, out var rawValue);
            var value = rawValue?.Trim() ?? string.Empty;
            if (field.Required && string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(string.Format(
                    语言服务.Current.Get("disk.required", "“{0}”不能为空。"), field.Label));

            switch (field.Mode)
            {
                case 磁盘字段模式.Positional:
                    if (value.Length > 0) positional.Add(value);
                    break;
                case 磁盘字段模式.MultiPositional:
                    positional.AddRange(SplitList(value));
                    break;
                case 磁盘字段模式.OptionValue:
                    if (value.Length == 0 || value == "none" && field.Id == "repair") break;
                    options.Add(field.Argument);
                    options.Add(value);
                    break;
                case 磁盘字段模式.MultiOptionValue:
                    foreach (var item in SplitList(value))
                    {
                        options.Add(field.Argument);
                        options.Add(item);
                    }
                    break;
                case 磁盘字段模式.Flag:
                    if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        if (command.Name == "rebase" && field.Id == "clearBacking")
                            options.AddRange(["-b", string.Empty]);
                        else
                            options.Add(field.Argument);
                    }
                    break;
                case 磁盘字段模式.Assignment:
                    if (value.Length > 0) options.Add(field.Argument + value);
                    break;
                case 磁盘字段模式.ChoiceArguments:
                    if (value.Length > 0) options.AddRange(命令行.Split(value));
                    break;
                case 磁盘字段模式.GlobalOptionValue:
                    if (value.Length > 0)
                    {
                        global.Add(field.Argument);
                        global.Add(value);
                    }
                    break;
                case 磁盘字段模式.RawArguments:
                    options.AddRange(命令行.Split(value));
                    break;
            }
        }

        options.AddRange(positional);
        var all = global.Concat([command.Name]).Concat(options).Select(命令行.Quote);
        return new 磁盘命令参数(global, options, "qemu-img " + string.Join(' ', all));
    }

    public Task<进程结果> ExecuteAsync(
        QEMU安装 install,
        磁盘命令 command,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        var built = BuildArgs(command, values);
        var arguments = built.GlobalArgs.Concat([command.Name]).Concat(built.CmdArgs);
        return 进程.RunAsync(install.ImgToolPath, arguments, cancellationToken);
    }

    public async Task<磁盘镜像信息?> GetInfoAsync(QEMU安装 install, string path)
    {
        if (!File.Exists(path)) return null;
        var result = await 进程.RunAsync(install.ImgToolPath, ["info", "--output", "json", path]);
        if (result.ExitCode != 0) return null;
        try
        {
            using var document = JsonDocument.Parse(result.Output);
            var root = document.RootElement;
            return new 磁盘镜像信息
            {
                Format = GetString(root, "format", "unknown"),
                VirtualSize = GetInt64(root, "virtual-size"),
                ActualSize = GetInt64(root, "actual-size"),
                BackingFile = GetString(root, "backing-filename", string.Empty)
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void HandleCompoundActions(
        磁盘命令 command,
        IReadOnlyDictionary<string, string> values,
        ICollection<string> options,
        ISet<string> consumed)
    {
        if (!values.TryGetValue("action", out var action) || string.IsNullOrWhiteSpace(action)) return;

        if (command.Name == "snapshot")
        {
            options.Add(action);
            consumed.Add("action");
            if (action != "-l" && values.TryGetValue("snapshot", out var snapshot) && !string.IsNullOrWhiteSpace(snapshot))
            {
                options.Add(snapshot.Trim());
                consumed.Add("snapshot");
            }
        }
        else if (command.Name == "bitmap" && action == "--merge")
        {
            options.Add(action);
            if (values.TryGetValue("mergeSource", out var source) && !string.IsNullOrWhiteSpace(source))
                options.Add(source.Trim());
            consumed.Add("action");
            consumed.Add("mergeSource");
        }
    }

    private static bool DependencyMatches(磁盘字段 field, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(field.DependsOnId)) return true;
        values.TryGetValue(field.DependsOnId, out var current);
        var equal = string.Equals(current, field.DependsOnValue, StringComparison.OrdinalIgnoreCase);
        return field.InvertDependency ? !equal : equal;
    }

    private static IEnumerable<string> SplitList(string value) => value
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string GetString(JsonElement element, string name, string fallback) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static long GetInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : 0;
}
