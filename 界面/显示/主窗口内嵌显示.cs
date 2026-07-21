using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.数据;
using QemuWG.服务;
using QemuWG.界面.显示;

namespace QemuWG;

public sealed partial class 主窗
{
    private readonly SemaphoreSlim dialogGate = new(1, 1);
    private CancellationTokenSource? displayConnection;
    private D3D11内嵌显示? embeddedDisplay;
    private DBus显示传输? embeddedTransport;
    private 虚拟机配置? displayMachine;
    private int embeddedDisplayWidth;
    private int embeddedDisplayHeight;
    private int displayVersion;
    private string failedDisplayMachineId = string.Empty;

    private async Task RefreshDisplayAsync(虚拟机配置? vm)
    {
        if (vm is not null && vm.IsRunning
            && string.Equals(vm.DisplayBackend, "dbus", StringComparison.OrdinalIgnoreCase)
            && string.Equals(failedDisplayMachineId, vm.Id, StringComparison.Ordinal))
        {
            DisplayStateText.Text = T("main.displayUnavailable", "无法连接虚拟机显示器");
            EmbeddedDisplayPanel.Visibility = Visibility.Collapsed;
            DisplayFallback.Visibility = Visibility.Visible;
            return;
        }

        if (vm is not null && vm.IsRunning
            && string.Equals(vm.DisplayBackend, "dbus", StringComparison.OrdinalIgnoreCase)
            && ReferenceEquals(displayMachine, vm)
            && displayConnection is { IsCancellationRequested: false })
        {
            if (embeddedDisplay?.已准备 == true)
            {
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
            EmbeddedDisplayPanel.Visibility = Visibility.Collapsed;
            DisplayFallback.Visibility = Visibility.Visible;
            if (vm?.IsRunning == true)
                DisplayStateText.Text = T("main.displayNativeWindow", "虚拟机正在 QEMU 原生窗口中显示");
            return;
        }

        StopDisplay(false);
        failedDisplayMachineId = string.Empty;
        displayMachine = vm;
        var connectionCancellation = new CancellationTokenSource();
        displayConnection = connectionCancellation;
        var renderer = new D3D11内嵌显示(EmbeddedDisplayPanel);
        embeddedDisplay = renderer;
        EmbeddedDisplayPanel.Visibility = Visibility.Collapsed;
        DisplayStateText.Text = T("main.displayConnecting", "正在连接虚拟机显示器…");
        DisplayFallback.Visibility = Visibility.Visible;

        try
        {
            var transport = await sessions.连接内嵌显示(vm, transport =>
            {
                transport.收到D3D11纹理 = (scanout, _) =>
                {
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

            if (version != Volatile.Read(ref displayVersion)
                || !ReferenceEquals(displayConnection, connectionCancellation)
                || !ReferenceEquals(embeddedDisplay, renderer))
            {
                return;
            }
            embeddedTransport = transport;
            failedDisplayMachineId = string.Empty;
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
                var failedTransport = Interlocked.Exchange(ref embeddedTransport, null);
                if (failedTransport is not null) _ = failedTransport.DisposeAsync();
                _ = 安全断开内嵌显示(vm);
                DisplayStateText.Text = T("main.displayUnavailable", "无法连接虚拟机显示器");
                EmbeddedDisplayPanel.Visibility = Visibility.Collapsed;
                DisplayFallback.Visibility = Visibility.Visible;
            }
        }
    }

    private void 设置内嵌显示尺寸(uint width, uint height)
    {
        Volatile.Write(ref embeddedDisplayWidth, checked((int)width));
        Volatile.Write(ref embeddedDisplayHeight, checked((int)height));
    }

    private void 显示内嵌画面(int version, D3D11内嵌显示 renderer)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (version != Volatile.Read(ref displayVersion)
                || !ReferenceEquals(embeddedDisplay, renderer)
                || !renderer.已准备) return;
            DisplayStateText.Text = T("main.displayRunning", "虚拟机显示器已连接");
            EmbeddedDisplayPanel.Visibility = Visibility.Visible;
            DisplayFallback.Visibility = Visibility.Collapsed;
        });
    }

    private void StopDisplay(bool invalidateVersion = true)
    {
        if (invalidateVersion) Interlocked.Increment(ref displayVersion);
        failedDisplayMachineId = string.Empty;
        var vm = displayMachine;
        displayMachine = null;
        var cancellation = Interlocked.Exchange(ref displayConnection, null);
        try { cancellation?.Cancel(); } catch { }
        cancellation?.Dispose();
        var renderer = Interlocked.Exchange(ref embeddedDisplay, null);
        var transport = Interlocked.Exchange(ref embeddedTransport, null);
        if (transport is not null) _ = transport.DisposeAsync();
        Volatile.Write(ref embeddedDisplayWidth, 0);
        Volatile.Write(ref embeddedDisplayHeight, 0);
        renderer?.Dispose();
        if (vm is not null) _ = 安全断开内嵌显示(vm);
    }

    private async Task 安全断开内嵌显示(虚拟机配置 vm)
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
            return await dialog.ShowAsync();
        }
        finally
        {
            dialogGate.Release();
        }
    }
}
