namespace QemuWG.data;

public sealed class DeviceSummary
{
    public DeviceSummary()
    {
    }

    public DeviceSummary(string glyph, string title, string value)
    {
        Glyph = glyph;
        Title = title;
        Value = value;
    }

    public string Glyph { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
