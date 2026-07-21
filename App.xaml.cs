using Microsoft.UI.Xaml;

namespace QemuWG;

public partial class 应用 : Application
{
    private Window? 主窗实例;

    public 应用()
    {
        应用日志.写("应用初始化");
        InitializeComponent();
        UnhandledException += (_, 参数) => 应用日志.写("未处理异常：" + 参数.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs 参数)
    {
        应用日志.写("应用启动");
        主窗实例 = new 主窗();
        应用日志.写("主窗已创建");
        主窗实例.Activate();
        应用日志.写("主窗已激活");
    }
}

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
