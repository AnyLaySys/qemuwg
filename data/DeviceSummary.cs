using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace QemuWG.data;

public sealed class DeviceSummary
{
    public DeviceSummary()
    {
    }

    public DeviceSummary(string glyph, string title, string value, Color color)
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
