using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.界面;

namespace QemuWG;

public sealed partial class 主窗
{
    private readonly 滚动激活保护 scrollActivationGuard;
    private ContentDialog? activeDialog;

    private void 主窗_滚动激活状态变化(object sender, WindowActivatedEventArgs args)
    {
        scrollActivationGuard.更新窗口状态(args.WindowActivationState, RootGrid, activeDialog);
    }
}
