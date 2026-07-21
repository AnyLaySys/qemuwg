using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU镜像
{
    public 磁盘命令参数 构建参数(
        磁盘命令 command,
        IReadOnlyDictionary<string, string> values)
    {
        var global = new List<string>();
        var options = new List<string>();
        var positional = new List<string>();
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        处理复合操作(command, values, options, consumed);

        foreach (var field in command.Fields)
        {
            if (consumed.Contains(field.Id) || !依赖匹配(field, values)) continue;
            values.TryGetValue(field.Id, out var rawValue);
            var value = rawValue?.Trim() ?? string.Empty;
            if (field.Required && string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(string.Format(
                    语言服务.当前.获取("disk.required", "“{0}”不能为空。"), field.Label));

            switch (field.Mode)
            {
                case 磁盘字段模式.Positional:
                    if (value.Length > 0) positional.Add(value);
                    break;
                case 磁盘字段模式.MultiPositional:
                    positional.AddRange(分割列表(value));
                    break;
                case 磁盘字段模式.OptionValue:
                    if (value.Length == 0 || value == "none" && field.Id == "repair") break;
                    options.Add(field.Argument);
                    options.Add(value);
                    break;
                case 磁盘字段模式.MultiOptionValue:
                    foreach (var item in 分割列表(value))
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
                    if (value.Length > 0) options.AddRange(命令行.分割(value));
                    break;
                case 磁盘字段模式.GlobalOptionValue:
                    if (value.Length > 0)
                    {
                        global.Add(field.Argument);
                        global.Add(value);
                    }
                    break;
                case 磁盘字段模式.RawArguments:
                    options.AddRange(命令行.分割(value));
                    break;
            }
        }

        options.AddRange(positional);
        var all = global.Concat([command.Name]).Concat(options).Select(命令行.引用);
        return new 磁盘命令参数(global, options, "qemu-img " + string.Join(' ', all));
    }

    private static void 处理复合操作(
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

    private static bool 依赖匹配(磁盘字段 field, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(field.DependsOnId)) return true;
        values.TryGetValue(field.DependsOnId, out var current);
        var equal = string.Equals(current, field.DependsOnValue, StringComparison.OrdinalIgnoreCase);
        return field.InvertDependency ? !equal : equal;
    }

    private static IEnumerable<string> 分割列表(string value) => value
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

}
