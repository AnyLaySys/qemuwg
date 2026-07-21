using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using QemuWG.数据;
using QemuWG.界面;

namespace QemuWG;

public sealed partial class 主窗
{
    private readonly VNC显示 displayClient = new();
    private readonly SemaphoreSlim dialogGate = new(1, 1);
    private readonly object displayFrameGate = new();
    private CancellationTokenSource? displayConnection;
    private 显示帧? pending显示帧;
    private WriteableBitmap? displayBitmap;
    private string displayMachineId = string.Empty;
    private int displayPort;
    private int displayVersion;
    private int displayFrameScheduled;

    private async Task RefreshDisplayAsync(虚拟机配置? vm)
    {
        var version = Interlocked.Increment(ref displayVersion);
        if (vm is null || !vm.IsRunning || !sessions.尝试获取显示端口(vm, out var port))
        {
            StopDisplay();
            DisplayImage.Visibility = Visibility.Collapsed;
            DisplayFallback.Visibility = Visibility.Visible;
            return;
        }

        if (displayClient.IsConnected && displayMachineId == vm.Id && displayPort == port)
        {
            DisplayImage.Visibility = Visibility.Visible;
            DisplayFallback.Visibility = Visibility.Collapsed;
            return;
        }

        StopDisplay();
        displayMachineId = vm.Id;
        displayPort = port;
        displayConnection = new CancellationTokenSource();
        DisplayImage.Visibility = Visibility.Collapsed;
        DisplayStateText.Text = T("main.displayConnecting", "正在连接虚拟机显示器…");
        DisplayFallback.Visibility = Visibility.Visible;

        try
        {
            var connected = await displayClient.ConnectAsync(port, displayConnection.Token);
            if (version != Volatile.Read(ref displayVersion)) return;
            if (!connected)
                DisplayStateText.Text = T("main.displayUnavailable", "无法连接虚拟机显示器");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            应用日志.写("Display connection failed: " + exception);
            if (version == Volatile.Read(ref displayVersion))
                DisplayStateText.Text = T("main.displayUnavailable", "无法连接虚拟机显示器");
        }
    }

    private void StopDisplay()
    {
        displayMachineId = string.Empty;
        displayPort = 0;
        var cancellation = Interlocked.Exchange(ref displayConnection, null);
        try { cancellation?.Cancel(); } catch { }
        cancellation?.Dispose();
        displayClient.Disconnect();
        lock (displayFrameGate) pending显示帧 = null;
    }

    private void DisplayClient_FrameReady(object? sender, 显示帧 frame)
    {
        lock (displayFrameGate) pending显示帧 = frame;
        Schedule显示帧();
    }

    private void Schedule显示帧()
    {
        if (Interlocked.Exchange(ref displayFrameScheduled, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(RenderPending显示帧))
            Interlocked.Exchange(ref displayFrameScheduled, 0);
    }

    private void RenderPending显示帧()
    {
        显示帧? frame;
        lock (displayFrameGate)
        {
            frame = pending显示帧;
            pending显示帧 = null;
        }

        if (frame is not null)
        {
            if (displayBitmap is null || displayBitmap.PixelWidth != frame.Width || displayBitmap.PixelHeight != frame.Height)
            {
                displayBitmap = new WriteableBitmap(frame.Width, frame.Height);
                DisplayImage.Source = displayBitmap;
            }
            using var stream = displayBitmap.PixelBuffer.AsStream();
            stream.Position = 0;
            stream.Write(frame.Pixels, 0, frame.Pixels.Length);
            displayBitmap.Invalidate();
            DisplayImage.Visibility = Visibility.Visible;
            DisplayFallback.Visibility = Visibility.Collapsed;
        }

        Interlocked.Exchange(ref displayFrameScheduled, 0);
        lock (displayFrameGate)
        {
            if (pending显示帧 is not null) Schedule显示帧();
        }
    }

    private void DisplayClient_ConnectionClosed(object? sender, string message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (selectedVm?.IsRunning != true) return;
            DisplayImage.Visibility = Visibility.Collapsed;
            DisplayFallback.Visibility = Visibility.Visible;
            DisplayStateText.Text = T("main.displayUnavailable", "无法连接虚拟机显示器");
            应用日志.写("Display connection closed: " + message);
        });
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
