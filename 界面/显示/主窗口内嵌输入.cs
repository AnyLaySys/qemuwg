using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using QemuWG.服务;

namespace QemuWG;

public sealed partial class 主窗
{
    private 内嵌显示输入控制器? 内嵌输入控制器;

    private 内嵌显示输入控制器 获取内嵌输入控制器() =>
        内嵌输入控制器 ??= new 内嵌显示输入控制器(
            EmbeddedDisplayPanel,
            () => embeddedTransport,
            () => (Volatile.Read(ref embeddedDisplayWidth), Volatile.Read(ref embeddedDisplayHeight)));

    private void EmbeddedDisplayPanel_KeyDown(object sender, KeyRoutedEventArgs e) =>
        获取内嵌输入控制器().处理按键按下(e);

    private void EmbeddedDisplayPanel_KeyUp(object sender, KeyRoutedEventArgs e) =>
        获取内嵌输入控制器().处理按键释放(e);

    private void EmbeddedDisplayPanel_LostFocus(object sender, RoutedEventArgs e) =>
        获取内嵌输入控制器().处理失去焦点();

    private void EmbeddedDisplayPanel_PointerEntered(object sender, PointerRoutedEventArgs e) =>
        获取内嵌输入控制器().处理指针进入();

    private void EmbeddedDisplayPanel_PointerExited(object sender, PointerRoutedEventArgs e) =>
        获取内嵌输入控制器().处理指针离开();

    private void EmbeddedDisplayPanel_PointerMoved(object sender, PointerRoutedEventArgs e) =>
        获取内嵌输入控制器().处理指针移动(e);

    private void EmbeddedDisplayPanel_PointerPressed(object sender, PointerRoutedEventArgs e) =>
        获取内嵌输入控制器().处理指针按下(e);

    private void EmbeddedDisplayPanel_PointerReleased(object sender, PointerRoutedEventArgs e) =>
        获取内嵌输入控制器().处理指针释放(e);

    private void EmbeddedDisplayPanel_PointerWheelChanged(object sender, PointerRoutedEventArgs e) =>
        获取内嵌输入控制器().处理滚轮(e);

    private void EmbeddedDisplayPanel_PointerCanceled(object sender, PointerRoutedEventArgs e) =>
        获取内嵌输入控制器().处理指针取消();

    private void EmbeddedDisplayPanel_PointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        获取内嵌输入控制器().处理捕获丢失();

    private void EmbeddedDisplayPanel_RightTapped(object sender, RightTappedRoutedEventArgs e) =>
        e.Handled = true;

    private Task 初始化内嵌输入(DBus显示传输 transport, CancellationToken cancellationToken) =>
        获取内嵌输入控制器().初始化(transport, cancellationToken);

    private void 重置内嵌输入状态() => 内嵌输入控制器?.重置();
}
