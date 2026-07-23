using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.数据;
using QemuWG.服务;
using QemuWG.界面;
using QemuWG.界面.显示;

namespace QemuWG;

public sealed partial class 主窗
{
    private readonly SemaphoreSlim dialogGate = new(1, 1);
    private CancellationTokenSource? displayConnection;
    private D3D11内嵌显示? embeddedDisplay;
    private DBus显示传输? embeddedTransport;
    private 仿真配置? displayMachine;
    private int embeddedDisplayWidth;
    private int embeddedDisplayHeight;
    private int displayVersion;
    private int loggedPresentedVersion;
    private string failedDisplayMachineId = string.Empty;
    private long displayRetryNotBefore;

    private async Task RefreshDisplayAsync(仿真配置? vm)
    {
        if (vm is not null && vm.IsRunning
            && string.Equals(vm.DisplayBackend, "dbus", StringComparison.OrdinalIgnoreCase)
            && string.Equals(failedDisplayMachineId, vm.Id, StringComparison.Ordinal)
            && Environment.TickCount64 < Volatile.Read(ref displayRetryNotBefore))
        {
            显示占位状态(T("main.displayUnavailable", "无法连接仿真显示器"));
            return;
        }

        if (vm is not null && vm.IsRunning
            && string.Equals(vm.DisplayBackend, "dbus", StringComparison.OrdinalIgnoreCase)
            && ReferenceEquals(displayMachine, vm)
            && displayConnection is { IsCancellationRequested: false })
        {
            if (embeddedDisplay?.已准备 == true)
            {
                DisplayStateText.Text = T("main.displayRunning", "仿真显示器已连接");
                EmbeddedDisplayPanel.Visibility = Visibility.Visible;
                DisplayFallback.Visibility = Visibility.Collapsed;
            }
            return;
        }

        var version = Interlocked.Increment(ref displayVersion);
        if (vm is null || !vm.IsRunning
            || !string.Equals(vm.DisplayBackend, "dbus", StringComparison.OrdinalIgnoreCase))
        {
            StopDisplay(false);
            显示占位状态(vm?.IsRunning == true
                ? T("main.displayNativeWindow", "仿真正在 QEMU 原生窗口中显示")
                : T("main.displayOff", "仿真已关机"));
            return;
        }

        StopDisplay(false);
        failedDisplayMachineId = string.Empty;
        Volatile.Write(ref displayRetryNotBefore, 0);
        displayMachine = vm;
        var connectionCancellation = new CancellationTokenSource();
        displayConnection = connectionCancellation;
        var renderer = new D3D11内嵌显示(EmbeddedDisplayPanel);
        embeddedDisplay = renderer;
        显示占位状态(T("main.displayConnecting", "正在连接仿真显示器…"));
        var connectStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        var firstCallbackLogged = 0;
        应用日志.写($"D-Bus display connect started: vm={vm.Id}, version={version}.");

        void 记录首个显示回调(string callback)
        {
            if (Interlocked.Exchange(ref firstCallbackLogged, 1) != 0) return;
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(connectStarted).TotalMilliseconds;
            应用日志.写($"D-Bus first display callback: {callback}, elapsed={elapsed:F1} ms.");
        }

        try
        {
            var transport = await sessions.连接内嵌显示(vm, transport =>
            {
                transport.收到D3D11纹理 = (scanout, _) =>
                {
                    记录首个显示回调("ScanoutTexture2d");
                    设置内嵌显示尺寸(scanout.显示宽度 == 0 ? scanout.纹理宽度 : scanout.显示宽度,
                        scanout.显示高度 == 0 ? scanout.纹理高度 : scanout.显示高度);
                    renderer.接收共享纹理(scanout);
                    显示内嵌画面(version, renderer);
                    return ValueTask.CompletedTask;
                };
                transport.更新D3D11纹理 = (region, _) =>
                {
                    renderer.更新共享纹理(region);
                    显示内嵌画面(version, renderer);
                    return ValueTask.CompletedTask;
                };
                transport.收到共享映射 = (scanout, _) =>
                {
                    记录首个显示回调("ScanoutMap");
                    设置内嵌显示尺寸(scanout.宽度, scanout.高度);
                    renderer.接收共享映射(scanout);
                    显示内嵌画面(version, renderer);
                    return ValueTask.CompletedTask;
                };
                transport.更新共享映射 = (region, _) =>
                {
                    renderer.更新共享映射(region);
                    显示内嵌画面(version, renderer);
                    return ValueTask.CompletedTask;
                };
                transport.收到位图 = (scanout, _) =>
                {
                    记录首个显示回调("Scanout");
                    设置内嵌显示尺寸(scanout.宽度, scanout.高度);
                    renderer.接收位图(scanout);
                    显示内嵌画面(version, renderer);
                    return ValueTask.CompletedTask;
                };
                transport.更新位图 = (update, _) =>
                {
                    renderer.更新位图(update);
                    显示内嵌画面(version, renderer);
                    return ValueTask.CompletedTask;
                };
                transport.停用显示 = _ =>
                {
                    renderer.停用显示();
                    return ValueTask.CompletedTask;
                };
            }, connectionCancellation.Token);

            应用日志.写($"D-Bus display listener registered: elapsed={System.Diagnostics.Stopwatch.GetElapsedTime(connectStarted).TotalMilliseconds:F1} ms.");

            if (version != Volatile.Read(ref displayVersion)
                || !ReferenceEquals(displayConnection, connectionCancellation)
                || !ReferenceEquals(embeddedDisplay, renderer))
            {
                return;
            }
            embeddedTransport = transport;
            failedDisplayMachineId = string.Empty;
            await 初始化内嵌输入(transport, connectionCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            应用日志.写("D-Bus embedded display connection failed: " + exception);
            if (version == Volatile.Read(ref displayVersion))
            {
                failedDisplayMachineId = vm.Id;
                Volatile.Write(ref displayRetryNotBefore, Environment.TickCount64 + 500);
                if (ReferenceEquals(displayConnection, connectionCancellation))
                {
                    displayConnection = null;
                    try { connectionCancellation.Cancel(); } catch { }
                    connectionCancellation.Dispose();
                }
                if (ReferenceEquals(embeddedDisplay, renderer))
                {
                    embeddedDisplay = null;
                    renderer.Dispose();
                }
                Interlocked.Exchange(ref embeddedTransport, null);
                _ = 安全断开内嵌显示(vm);
                显示占位状态(T("main.displayUnavailable", "无法连接仿真显示器"));
                _ = 安排内嵌显示重试(vm, version);
            }
        }
    }

    private async Task 安排内嵌显示重试(仿真配置 vm, int failedVersion)
    {
        await Task.Delay(500);
        if (failedVersion != Volatile.Read(ref displayVersion)
            || !vm.IsRunning
            || !ReferenceEquals(selectedVm, vm)) return;
        DispatcherQueue.TryEnqueue(() => _ = RefreshDisplayAsync(vm));
    }

    private void 显示占位状态(string text)
    {
        var animate = DisplayFallback.Visibility != Visibility.Visible
                      || !string.Equals(DisplayStateText.Text, text, StringComparison.Ordinal);
        DisplayStateText.Text = text;
        EmbeddedDisplayPanel.Visibility = Visibility.Collapsed;
        DisplayFallback.Visibility = Visibility.Visible;
        if (animate) _ = 页面过渡动画.渐进显示(DisplayFallback, 4);
    }

    private void 设置内嵌显示尺寸(uint width, uint height)
    {
        Volatile.Write(ref embeddedDisplayWidth, checked((int)width));
        Volatile.Write(ref embeddedDisplayHeight, checked((int)height));
        DispatcherQueue.TryEnqueue(更新内嵌画面布局);
    }

    private void 更新内嵌画面布局()
    {
        var guestWidth = Volatile.Read(ref embeddedDisplayWidth);
        var guestHeight = Volatile.Read(ref embeddedDisplayHeight);
        var availableWidth = Math.Max(0, DisplaySurface.ActualWidth);
        var availableHeight = Math.Max(0, DisplaySurface.ActualHeight);
        if (guestWidth <= 0 || guestHeight <= 0 || availableWidth <= 0 || availableHeight <= 0) return;

        var scale = Math.Min(availableWidth / guestWidth, availableHeight / guestHeight);
        EmbeddedDisplayHost.Width = Math.Max(1, guestWidth * scale);
        EmbeddedDisplayHost.Height = Math.Max(1, guestHeight * scale);
    }

    private void 显示内嵌画面(int version, D3D11内嵌显示 renderer)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (version != Volatile.Read(ref displayVersion)
                || !ReferenceEquals(embeddedDisplay, renderer)
                || !renderer.已准备) return;
            DisplayStateText.Text = T("main.displayRunning", "仿真显示器已连接");
            EmbeddedDisplayPanel.Visibility = Visibility.Visible;
            DisplayFallback.Visibility = Visibility.Collapsed;
            if (Volatile.Read(ref loggedPresentedVersion) != version)
            {
                Volatile.Write(ref loggedPresentedVersion, version);
                应用日志.写($"D-Bus display presented in SwapChainPanel: version={version}.");
            }
        });
    }

    private void StopDisplay(bool invalidateVersion = true)
    {
        if (invalidateVersion) Interlocked.Increment(ref displayVersion);
        failedDisplayMachineId = string.Empty;
        Volatile.Write(ref displayRetryNotBefore, 0);
        var vm = displayMachine;
        displayMachine = null;
        var cancellation = Interlocked.Exchange(ref displayConnection, null);
        try { cancellation?.Cancel(); } catch { }
        cancellation?.Dispose();
        var renderer = Interlocked.Exchange(ref embeddedDisplay, null);
        Interlocked.Exchange(ref embeddedTransport, null);
        重置内嵌输入状态();
        Volatile.Write(ref embeddedDisplayWidth, 0);
        Volatile.Write(ref embeddedDisplayHeight, 0);
        renderer?.Dispose();
        if (vm is not null) _ = 安全断开内嵌显示(vm);
    }

    private async Task 安全断开内嵌显示(仿真配置 vm)
    {
        try
        {
            await sessions.断开内嵌显示(vm);
        }
        catch (Exception exception)
        {
            应用日志.写("Disconnecting D-Bus embedded display failed: " + exception.Message);
        }
    }

    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        await dialogGate.WaitAsync();
        try
        {
            dialog.XamlRoot ??= RootXamlRoot;
            对话框布局.应用按钮圆角(dialog);
            activeDialog = dialog;
            dialog.Opened += (_, _) =>
            {
                if (dialog.Content is DependencyObject content) 按钮交互动画.启用(content);
            };
            return await dialog.ShowAsync();
        }
        finally
        {
            if (ReferenceEquals(activeDialog, dialog)) activeDialog = null;
            dialogGate.Release();
        }
    }
}
