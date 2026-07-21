using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using QemuWG.服务;
using PointerUpdateKind = Microsoft.UI.Input.PointerUpdateKind;

namespace QemuWG;

public sealed partial class 主窗
{
    private int pendingMouseX;
    private int pendingMouseY;
    private int mouseMotionScheduled;
    private long mouseMotionVersion;

    private void EmbeddedDisplayPanel_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.KeyStatus.WasKeyDown) return;
        var keycode = (uint)e.KeyStatus.ScanCode;
        if (e.KeyStatus.IsExtendedKey) keycode |= 0x80;
        if (keycode == 0) return;
        e.Handled = true;
        发送输入(transport => transport.按下按键(keycode));
    }

    private void EmbeddedDisplayPanel_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        var keycode = (uint)e.KeyStatus.ScanCode;
        if (e.KeyStatus.IsExtendedKey) keycode |= 0x80;
        if (keycode == 0) return;
        e.Handled = true;
        发送输入(transport => transport.释放按键(keycode));
    }

    private void EmbeddedDisplayPanel_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(EmbeddedDisplayPanel).Position;
        var width = Volatile.Read(ref embeddedDisplayWidth);
        var height = Volatile.Read(ref embeddedDisplayHeight);
        if (width <= 0 || height <= 0) return;
        Volatile.Write(ref pendingMouseX, Math.Clamp((int)Math.Round(point.X), 0, width - 1));
        Volatile.Write(ref pendingMouseY, Math.Clamp((int)Math.Round(point.Y), 0, height - 1));
        Interlocked.Increment(ref mouseMotionVersion);
        安排鼠标移动();
    }

    private void EmbeddedDisplayPanel_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        EmbeddedDisplayPanel.Focus(FocusState.Pointer);
        var kind = e.GetCurrentPoint(EmbeddedDisplayPanel).Properties.PointerUpdateKind;
        var button = kind switch
        {
            PointerUpdateKind.LeftButtonPressed => 0u,
            PointerUpdateKind.MiddleButtonPressed => 1u,
            PointerUpdateKind.RightButtonPressed => 2u,
            PointerUpdateKind.XButton1Pressed => 5u,
            PointerUpdateKind.XButton2Pressed => 6u,
            _ => uint.MaxValue
        };
        if (button == uint.MaxValue) return;
        e.Handled = true;
        发送输入(transport => transport.按下鼠标按钮(button));
    }

    private void EmbeddedDisplayPanel_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var kind = e.GetCurrentPoint(EmbeddedDisplayPanel).Properties.PointerUpdateKind;
        var button = kind switch
        {
            PointerUpdateKind.LeftButtonReleased => 0u,
            PointerUpdateKind.MiddleButtonReleased => 1u,
            PointerUpdateKind.RightButtonReleased => 2u,
            PointerUpdateKind.XButton1Released => 5u,
            PointerUpdateKind.XButton2Released => 6u,
            _ => uint.MaxValue
        };
        if (button == uint.MaxValue) return;
        e.Handled = true;
        发送输入(transport => transport.释放鼠标按钮(button));
    }

    private void EmbeddedDisplayPanel_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(EmbeddedDisplayPanel).Properties.MouseWheelDelta;
        if (delta == 0) return;
        e.Handled = true;
        var button = delta > 0 ? 3u : 4u;
        发送输入(async transport =>
        {
            await transport.按下鼠标按钮(button);
            await transport.释放鼠标按钮(button);
        });
    }

    private void 安排鼠标移动()
    {
        if (Interlocked.Exchange(ref mouseMotionScheduled, 1) != 0) return;
        _ = Task.Run(async () =>
        {
            long processedVersion = -1;
            try
            {
                while (true)
                {
                    processedVersion = Volatile.Read(ref mouseMotionVersion);
                    var x = Volatile.Read(ref pendingMouseX);
                    var y = Volatile.Read(ref pendingMouseY);
                    var transport = embeddedTransport;
                    if (transport is null) return;
                    await transport.设置鼠标位置((uint)x, (uint)y);
                    if (processedVersion == Volatile.Read(ref mouseMotionVersion)) return;
                }
            }
            catch (Exception exception)
            {
                应用日志.写("D-Bus mouse input failed: " + exception.Message);
            }
            finally
            {
                Interlocked.Exchange(ref mouseMotionScheduled, 0);
                if (embeddedTransport is not null && processedVersion != Volatile.Read(ref mouseMotionVersion))
                    安排鼠标移动();
            }
        });
    }

    private void 发送输入(Func<DBus显示传输, Task> action)
    {
        var transport = embeddedTransport;
        if (transport is null) return;
        _ = 发送输入Core(transport, action);
    }

    private static async Task 发送输入Core(DBus显示传输 transport, Func<DBus显示传输, Task> action)
    {
        try
        {
            await action(transport);
        }
        catch (Exception exception)
        {
            应用日志.写("D-Bus embedded input failed: " + exception.Message);
        }
    }
}
