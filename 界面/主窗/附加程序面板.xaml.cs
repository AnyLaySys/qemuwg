using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.数据;
using QemuWG.服务;

namespace QemuWG.界面;

public sealed partial class QEMU附加功能 : UserControl
{
    private static string T(string key, string fallback) => 语言服务.当前.获取(key, fallback);

    private readonly nint ownerHandle;
    private readonly QEMU安装 install;
    private readonly QEMU工具会话 sessions;
    private readonly QEMU工具服务 tools = new();
    private bool initialized;

    public QEMU附加功能(nint ownerHandle, QEMU安装 install, QEMU工具会话 sessions)
    {
        InitializeComponent();
        页面过渡动画.启用标签页动画(ToolsTabs);
        Loaded += (_, _) => 按钮交互动画.启用(this);
        this.ownerHandle = ownerHandle;
        this.install = install;
        this.sessions = sessions;
        sessions.收到输出 += Sessions_OutputReceived;
        Unloaded += (_, _) =>
        {
            sessions.收到输出 -= Sessions_OutputReceived;
            ioCancellation?.Cancel();
            toolCancellation?.Cancel();
        };
        Loaded += InitializeOnFirstLoad;
    }

    private async void InitializeOnFirstLoad(object sender, RoutedEventArgs args)
    {
        if (initialized) return;
        initialized = true;
        try { await InitializeImageIoAsync(); }
        catch (Exception exception) { 应用日志.写("QEMU附加功能 initialization failed: " + exception); }
    }

    private void Sessions_OutputReceived(object? sender, 工具输出事件 e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (string.Equals(e.Tool, "qemu-nbd.exe", StringComparison.OrdinalIgnoreCase))
            NbdOutputBox.Text += (NbdOutputBox.Text.Length == 0 ? string.Empty : Environment.NewLine) + e.Text;
        if (string.Equals(e.Tool, "qemu-storage-daemon.exe", StringComparison.OrdinalIgnoreCase))
            StorageOutputBox.Text += (StorageOutputBox.Text.Length == 0 ? string.Empty : Environment.NewLine) + e.Text;
        if (string.Equals(e.Tool, SelectedTool, StringComparison.OrdinalIgnoreCase)) AppendOutput(e.Text);
    });

    private static string FormatResult(进程结果 result) => string.Format(T("tools.exitCode", "退出码 {0}"), result.退出码) + Environment.NewLine + (string.IsNullOrWhiteSpace(result.输出) ? T("tools.noOutput", "（无输出）") : result.输出);
}
