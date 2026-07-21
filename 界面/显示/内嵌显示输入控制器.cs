using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QemuWG.服务;

namespace QemuWG;

internal sealed class 内嵌显示输入控制器(
    SwapChainPanel 显示面板,
    Func<DBus显示传输?> 获取传输,
    Func<(int 宽度, int 高度)> 获取来宾尺寸)
{
    private readonly SemaphoreSlim 输入门 = new(1, 1);
    private readonly object 输入队列锁 = new();
    private readonly object 按键锁 = new();
    private readonly HashSet<uint> 已按下按键 = [];
    private Task 输入队列 = Task.CompletedTask;

    private int 鼠标绝对模式 = -1;
    private long 鼠标模式上次检查时间;
    private int 重置鼠标基准 = 1;
    private double 上次来宾横坐标;
    private double 上次来宾纵坐标;
    private bool 有上次来宾坐标;

    private int 待发送鼠标横坐标;
    private int 待发送鼠标纵坐标;
    private int 待发送鼠标横向位移;
    private int 待发送鼠标纵向位移;
    private int 已安排鼠标移动;
    private long 鼠标移动版本;
    private long 已发送鼠标移动版本;
    private int 已按下鼠标按钮;
    private long 输入错误上次记录时间;

    public void 处理按键按下(KeyRoutedEventArgs e)
    {
        if (e.KeyStatus.WasKeyDown) return;
        var keycode = 获取按键码(e);
        if (keycode == 0) return;
        e.Handled = true;
        lock (按键锁) 已按下按键.Add(keycode);
        发送输入(transport => transport.按下按键(keycode));
    }

    public void 处理按键释放(KeyRoutedEventArgs e)
    {
        var keycode = 获取按键码(e);
        if (keycode == 0) return;
        e.Handled = true;
        lock (按键锁) 已按下按键.Remove(keycode);
        发送输入(transport => transport.释放按键(keycode));
    }

    public void 处理失去焦点()
    {
        释放捕获的鼠标按钮();
        uint[] keys;
        lock (按键锁)
        {
            keys = [.. 已按下按键];
            已按下按键.Clear();
        }
        if (keys.Length == 0) return;
        发送输入(async transport =>
        {
            foreach (var key in keys) await transport.释放按键(key);
        });
    }

    public void 处理指针进入() => Interlocked.Exchange(ref 重置鼠标基准, 1);

    public void 处理指针离开()
    {
        if (Volatile.Read(ref 已按下鼠标按钮) == 0)
            Interlocked.Exchange(ref 重置鼠标基准, 1);
    }

    public void 处理指针移动(PointerRoutedEventArgs e) => 更新待发送鼠标位置(e);

    public void 处理指针按下(PointerRoutedEventArgs e)
    {
        显示面板.Focus(FocusState.Pointer);
        显示面板.CapturePointer(e.Pointer);
        更新待发送鼠标位置(e);

        var button = 获取鼠标按钮(e, true);
        if (button == uint.MaxValue) return;
        e.Handled = true;
        Interlocked.Or(ref 已按下鼠标按钮, 1 << (int)button);
        发送输入(transport => transport.按下鼠标按钮(button));
    }

    public void 处理指针释放(PointerRoutedEventArgs e)
    {
        更新待发送鼠标位置(e);
        var button = 获取鼠标按钮(e, false);
        if (button == uint.MaxValue) return;
        e.Handled = true;

        var remainingButtons = Interlocked.And(ref 已按下鼠标按钮, ~(1 << (int)button))
            & ~(1 << (int)button);
        发送输入(transport => transport.释放鼠标按钮(button));
        if (remainingButtons == 0) 显示面板.ReleasePointerCapture(e.Pointer);
    }

    public void 处理滚轮(PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(显示面板).Properties.MouseWheelDelta;
        if (delta == 0) return;
        e.Handled = true;
        var button = delta > 0 ? 3u : 4u;
        var steps = Math.Clamp((int)Math.Max(1, Math.Abs((long)delta) / 120), 1, 8);
        发送输入(async transport =>
        {
            for (var index = 0; index < steps; index++)
            {
                await transport.按下鼠标按钮(button);
                await transport.释放鼠标按钮(button);
            }
        });
    }

    public void 处理指针取消() => 释放捕获的鼠标按钮();

    public void 处理捕获丢失() => 释放捕获的鼠标按钮();

    public async Task 初始化(DBus显示传输 transport, CancellationToken cancellationToken)
    {
        重置();
        await 输入门.WaitAsync(cancellationToken);
        try
        {
            if (!ReferenceEquals(获取传输(), transport)) return;
            var absolute = await transport.获取鼠标是否绝对定位(cancellationToken: cancellationToken);
            Volatile.Write(ref 鼠标绝对模式, absolute ? 1 : 0);
            Volatile.Write(ref 鼠标模式上次检查时间, Environment.TickCount64);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            记录输入错误(exception);
        }
        finally
        {
            输入门.Release();
        }
    }

    public void 重置()
    {
        Volatile.Write(ref 鼠标绝对模式, -1);
        Volatile.Write(ref 鼠标模式上次检查时间, 0);
        Interlocked.Exchange(ref 重置鼠标基准, 1);
        Interlocked.Exchange(ref 待发送鼠标横向位移, 0);
        Interlocked.Exchange(ref 待发送鼠标纵向位移, 0);
        Interlocked.Exchange(ref 已按下鼠标按钮, 0);
        lock (按键锁) 已按下按键.Clear();
        var version = Interlocked.Increment(ref 鼠标移动版本);
        Volatile.Write(ref 已发送鼠标移动版本, version);
    }

    private static uint 获取按键码(KeyRoutedEventArgs e)
    {
        var keycode = (uint)e.KeyStatus.ScanCode;
        if (e.KeyStatus.IsExtendedKey) keycode |= 0x80;
        return keycode;
    }

    private void 更新待发送鼠标位置(PointerRoutedEventArgs e)
    {
        if (!尝试转换为来宾坐标(e, out var guestX, out var guestY)) return;
        if (Interlocked.Exchange(ref 重置鼠标基准, 0) != 0) 有上次来宾坐标 = false;

        var (width, height) = 获取来宾尺寸();
        Volatile.Write(ref 待发送鼠标横坐标, Math.Clamp((int)Math.Round(guestX), 0, width - 1));
        Volatile.Write(ref 待发送鼠标纵坐标, Math.Clamp((int)Math.Round(guestY), 0, height - 1));

        if (有上次来宾坐标)
        {
            var deltaX = (int)Math.Round(guestX - 上次来宾横坐标);
            var deltaY = (int)Math.Round(guestY - 上次来宾纵坐标);
            if (deltaX != 0) Interlocked.Add(ref 待发送鼠标横向位移, deltaX);
            if (deltaY != 0) Interlocked.Add(ref 待发送鼠标纵向位移, deltaY);
        }

        上次来宾横坐标 = guestX;
        上次来宾纵坐标 = guestY;
        有上次来宾坐标 = true;
        Interlocked.Increment(ref 鼠标移动版本);
        安排鼠标移动();
    }

    private bool 尝试转换为来宾坐标(PointerRoutedEventArgs e, out double guestX, out double guestY)
    {
        var (width, height) = 获取来宾尺寸();
        var panelWidth = 显示面板.ActualWidth;
        var panelHeight = 显示面板.ActualHeight;
        if (width <= 0 || height <= 0 || panelWidth <= 0 || panelHeight <= 0)
        {
            guestX = 0;
            guestY = 0;
            return false;
        }

        var point = e.GetCurrentPoint(显示面板).Position;
        guestX = point.X * width / panelWidth;
        guestY = point.Y * height / panelHeight;
        return true;
    }

    private static uint 获取鼠标按钮(PointerRoutedEventArgs e, bool pressed)
    {
        var kind = e.GetCurrentPoint((UIElement)e.OriginalSource).Properties.PointerUpdateKind;
        return (pressed, kind) switch
        {
            (true, PointerUpdateKind.LeftButtonPressed) or (false, PointerUpdateKind.LeftButtonReleased) => 0u,
            (true, PointerUpdateKind.MiddleButtonPressed) or (false, PointerUpdateKind.MiddleButtonReleased) => 1u,
            (true, PointerUpdateKind.RightButtonPressed) or (false, PointerUpdateKind.RightButtonReleased) => 2u,
            (true, PointerUpdateKind.XButton1Pressed) or (false, PointerUpdateKind.XButton1Released) => 5u,
            (true, PointerUpdateKind.XButton2Pressed) or (false, PointerUpdateKind.XButton2Released) => 6u,
            _ => uint.MaxValue
        };
    }

    private void 释放捕获的鼠标按钮()
    {
        Interlocked.Exchange(ref 重置鼠标基准, 1);
        var buttons = Interlocked.Exchange(ref 已按下鼠标按钮, 0);
        if (buttons == 0) return;
        发送输入(async transport =>
        {
            foreach (var button in new uint[] { 0, 1, 2, 5, 6 })
            {
                if ((buttons & (1 << (int)button)) != 0)
                    await transport.释放鼠标按钮(button);
            }
        });
    }

    private void 安排鼠标移动()
    {
        if (Interlocked.Exchange(ref 已安排鼠标移动, 1) != 0) return;
        var transport = 获取传输();
        if (transport is null)
        {
            Interlocked.Exchange(ref 已安排鼠标移动, 0);
            return;
        }

        var requiredVersion = Volatile.Read(ref 鼠标移动版本);
        var queuedTask = 排队输入(transport, requiredVersion, null);
        _ = 完成鼠标移动请求(queuedTask, transport);
    }

    private async Task 完成鼠标移动请求(Task queuedTask, DBus显示传输 transport)
    {
        try
        {
            await queuedTask;
        }
        finally
        {
            Interlocked.Exchange(ref 已安排鼠标移动, 0);
            if (ReferenceEquals(获取传输(), transport)
                && Volatile.Read(ref 已发送鼠标移动版本) != Volatile.Read(ref 鼠标移动版本))
            {
                安排鼠标移动();
            }
        }
    }

    private void 发送输入(Func<DBus显示传输, Task> action)
    {
        var transport = 获取传输();
        if (transport is null) return;
        _ = 排队输入(transport, Volatile.Read(ref 鼠标移动版本), action);
    }

    private Task 排队输入(
        DBus显示传输 transport,
        long requiredMotionVersion,
        Func<DBus显示传输, Task>? action)
    {
        lock (输入队列锁)
        {
            输入队列 = 输入队列.ContinueWith(
                _ => 处理排队输入(transport, requiredMotionVersion, action),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default).Unwrap();
            return 输入队列;
        }
    }

    private async Task 处理排队输入(
        DBus显示传输 transport,
        long requiredMotionVersion,
        Func<DBus显示传输, Task>? action)
    {
        await 输入门.WaitAsync();
        try
        {
            if (!ReferenceEquals(获取传输(), transport)) return;
            if (Volatile.Read(ref 已发送鼠标移动版本) < requiredMotionVersion)
                await 发送待处理鼠标移动(transport);
            if (action is not null && ReferenceEquals(获取传输(), transport))
                await action(transport);
        }
        catch (Exception exception)
        {
            记录输入错误(exception);
        }
        finally
        {
            输入门.Release();
        }
    }

    private async Task 发送待处理鼠标移动(DBus显示传输 transport)
    {
        var version = Volatile.Read(ref 鼠标移动版本);
        if (await 获取鼠标绝对模式(transport))
        {
            var x = Volatile.Read(ref 待发送鼠标横坐标);
            var y = Volatile.Read(ref 待发送鼠标纵坐标);
            await transport.设置鼠标位置((uint)x, (uint)y);
            Interlocked.Exchange(ref 待发送鼠标横向位移, 0);
            Interlocked.Exchange(ref 待发送鼠标纵向位移, 0);
        }
        else
        {
            var deltaX = Interlocked.Exchange(ref 待发送鼠标横向位移, 0);
            var deltaY = Interlocked.Exchange(ref 待发送鼠标纵向位移, 0);
            if (deltaX != 0 || deltaY != 0) await transport.移动鼠标相对(deltaX, deltaY);
        }
        Volatile.Write(ref 已发送鼠标移动版本, version);
    }

    private async Task<bool> 获取鼠标绝对模式(DBus显示传输 transport)
    {
        var now = Environment.TickCount64;
        var cachedMode = Volatile.Read(ref 鼠标绝对模式);
        var lastChecked = Volatile.Read(ref 鼠标模式上次检查时间);
        if (cachedMode >= 0 && now - lastChecked < 1000) return cachedMode == 1;

        var absolute = await transport.获取鼠标是否绝对定位();
        var newMode = absolute ? 1 : 0;
        var previousMode = Interlocked.Exchange(ref 鼠标绝对模式, newMode);
        Volatile.Write(ref 鼠标模式上次检查时间, now);
        if (previousMode >= 0 && previousMode != newMode)
        {
            Interlocked.Exchange(ref 重置鼠标基准, 1);
            Interlocked.Exchange(ref 待发送鼠标横向位移, 0);
            Interlocked.Exchange(ref 待发送鼠标纵向位移, 0);
        }
        return absolute;
    }

    private void 记录输入错误(Exception exception)
    {
        var now = Environment.TickCount64;
        var last = Volatile.Read(ref 输入错误上次记录时间);
        if (now - last < 2000) return;
        Volatile.Write(ref 输入错误上次记录时间, now);
        应用日志.写("D-Bus embedded input failed: " + exception.Message);
    }
}
