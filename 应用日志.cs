namespace QemuWG;

internal static class 应用日志
{
    private static readonly string 路径 = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "qemuwg",
        "app.log");

    public static void 写(string 消息)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(路径)!);
            File.AppendAllText(路径, $"[{DateTime.Now:O}] {消息}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
