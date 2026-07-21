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
