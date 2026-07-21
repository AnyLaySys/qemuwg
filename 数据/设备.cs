using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace QemuWG.数据;

public sealed class 设备摘要
{
    public 设备摘要()
    {
    }

    public 设备摘要(string glyph, string title, string value, Color color)
    {
        Glyph = glyph;
        Title = title;
        Value = value;
        IconBrush = new SolidColorBrush(color);
    }

    public string Glyph { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public Brush? IconBrush { get; set; }
}
