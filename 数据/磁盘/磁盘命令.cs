using Microsoft.UI.Xaml.Media;

namespace QemuWG.数据;

public sealed class 磁盘命令
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string DisplayCategory { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Glyph { get; init; } = string.Empty;
    public Brush? IconBrush { get; init; }
    public bool CanWrite { get; init; }
    public IReadOnlyList<磁盘字段> Fields { get; init; } = [];
}
