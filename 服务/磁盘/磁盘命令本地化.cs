using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using QemuWG.数据;

namespace QemuWG.服务;

public static partial class QEMU镜像命令
{
    private static 磁盘命令 Cmd(string name, string category, string description, bool canWrite,
        params 磁盘字段[] fields)
    {
        var allFields = fields.Concat([
            Text("object", "对象定义（以分号分隔）", "--object", mode: 磁盘字段模式.MultiOptionValue, wide: true, advanced: true),
            Text("trace", "跟踪规则", "--trace", mode: 磁盘字段模式.GlobalOptionValue, wide: true, advanced: true),
            Text("extra", "额外参数", mode: 磁盘字段模式.RawArguments, wide: true, advanced: true)
        ]).Select(field => LocalizeField(name, field)).ToList();

        return new 磁盘命令
        {
            Name = name,
            DisplayName = 本地化.获取($"disk.command.{name}", name),
            Category = category,
            DisplayCategory = 本地化.获取(分类键(category), category),
            Description = 本地化.获取($"disk.description.{name}", description),
            Glyph = 命令图标(name),
            IconBrush = 命令画刷(name),
            CanWrite = canWrite,
            Fields = allFields
        };
    }

    private static 磁盘字段 LocalizeField(string command, 磁盘字段 field) => new()
    {
        Id = field.Id,
        Label = 本地化.获取($"disk.field.{command}.{field.Id}", 本地化.获取($"disk.field.{field.Id}", field.Label)),
        Mode = field.Mode,
        Argument = field.Argument,
        DefaultValue = field.DefaultValue,
        Choices = field.Choices,
        Required = field.Required,
        Wide = field.Wide,
        Advanced = field.Advanced,
        PathKind = field.PathKind,
        DependsOnId = field.DependsOnId,
        DependsOnValue = field.DependsOnValue,
        InvertDependency = field.InvertDependency
    };

    private static string 命令图标(string command) => command switch
    {
        "info" => "\uE946",
        "check" => "\uE9D9",
        "map" => "\uE81E",
        "measure" => "\uE9F9",
        "compare" => "\uE8AB",
        "bench" => "\uE9D2",
        "create" => "\uE710",
        "convert" => "\uE8AB",
        "dd" => "\uE8C8",
        "resize" => "\uE8B5",
        "rebase" => "\uE8A7",
        "commit" => "\uE74E",
        "amend" => "\uE70F",
        "bitmap" => "\uE91B",
        _ => "\uE958"
    };

    private static SolidColorBrush 命令画刷(string command)
    {
        var color = command switch
        {
            "info" or "map" => ColorHelper.FromArgb(255, 48, 138, 210),
            "check" or "compare" => ColorHelper.FromArgb(255, 38, 166, 154),
            "measure" or "bench" => ColorHelper.FromArgb(255, 125, 104, 210),
            "create" => ColorHelper.FromArgb(255, 46, 160, 87),
            "convert" or "dd" => ColorHelper.FromArgb(255, 225, 139, 40),
            "resize" or "amend" => ColorHelper.FromArgb(255, 218, 90, 116),
            "rebase" or "commit" => ColorHelper.FromArgb(255, 195, 92, 190),
            "bitmap" => ColorHelper.FromArgb(255, 80, 154, 128),
            _ => ColorHelper.FromArgb(255, 96, 110, 125)
        };
        return new SolidColorBrush(color);
    }

    private static string 分类键(string category) => category switch
    {
        "检查" => "disk.category.inspect",
        "创建" => "disk.category.create",
        "转换" => "disk.category.convert",
        "修改" => "disk.category.modify",
        "位图" => "disk.category.bitmap",
        _ => category
    };
}
