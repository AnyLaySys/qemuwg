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
    private readonly 仿真配置 machine;
    private bool initialized;
    private bool outputSubscribed;

    public QEMU附加功能(nint ownerHandle, QEMU安装 install, QEMU工具会话 sessions, 仿真配置 machine)
    {
        InitializeComponent();
        页面过渡动画.启用标签页动画(ToolsTabs);
        this.ownerHandle = ownerHandle;
        this.install = install;
        this.sessions = sessions;
        this.machine = machine;
        应用仿真默认值();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        按钮交互动画.启用(this);
        if (!outputSubscribed)
        {
            sessions.收到输出 += Sessions_OutputReceived;
            outputSubscribed = true;
        }
        if (initialized) return;
        initialized = true;
        try { await InitializeImageIoAsync(); }
        catch (Exception exception) { 应用日志.写("QEMU附加功能 initialization failed: " + exception); }
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (outputSubscribed)
        {
            sessions.收到输出 -= Sessions_OutputReceived;
            outputSubscribed = false;
        }
        ioCancellation?.Cancel();
    }

    private void 应用仿真默认值()
    {
        IoImageBox.Text = machine.DiskPath;
        NbdImageBox.Text = machine.DiskPath;
        IoReadOnlyToggle.IsOn = true;
        NbdReadOnlyToggle.IsOn = true;
        if (!string.IsNullOrWhiteSpace(machine.DiskPath))
        {
            var escapedDisk = machine.DiskPath.Replace(",", ",,");
            StorageBlockdevBox.Text = $"driver=file,node-name=vm-file,filename={escapedDisk};driver=qcow2,node-name=vm-disk,file=vm-file";
            StorageExportBox.Text = "type=nbd,id=vm-export,node-name=vm-disk,writable=off";
        }
        if (machine.DisplayWidth > 0)
        {
            EdidPreferredWidthBox.Value = machine.DisplayWidth;
            EdidMaximumWidthBox.Value = machine.DisplayWidth;
        }
        if (machine.DisplayHeight > 0)
        {
            EdidPreferredHeightBox.Value = machine.DisplayHeight;
            EdidMaximumHeightBox.Value = machine.DisplayHeight;
        }
        if (!string.IsNullOrWhiteSpace(machine.DirPath))
        {
            EdidOutputBox.Text = Path.Combine(machine.DirPath, "显示识别数据.edid");
            StoragePidFileBox.Text = Path.Combine(machine.DirPath, "存储守护进程.pid");
        }
    }

    private void Sessions_OutputReceived(object? sender, 工具输出事件 e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!string.Equals(e.Scope, machine.Id, StringComparison.OrdinalIgnoreCase)) return;
        if (string.Equals(e.Tool, "qemu-nbd.exe", StringComparison.OrdinalIgnoreCase))
            NbdOutputBox.Text += (NbdOutputBox.Text.Length == 0 ? string.Empty : Environment.NewLine) + e.Text;
        if (string.Equals(e.Tool, "qemu-storage-daemon.exe", StringComparison.OrdinalIgnoreCase))
            StorageOutputBox.Text += (StorageOutputBox.Text.Length == 0 ? string.Empty : Environment.NewLine) + e.Text;
    });

    private bool 仿真正在占用主磁盘(string? path) =>
        machine.IsRunning && 路径相同(path, machine.DiskPath);

    private bool 参数引用主磁盘(params string?[] values)
    {
        if (!machine.IsRunning || string.IsNullOrWhiteSpace(machine.DiskPath)) return false;
        var normalizedDisk = 规范路径文本(machine.DiskPath);
        return values.Any(value => !string.IsNullOrWhiteSpace(value)
                                   && 规范路径文本(value).Contains(normalizedDisk, StringComparison.OrdinalIgnoreCase));
    }

    private static bool 路径相同(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase); }
    }

    private static string 规范路径文本(string value)
    {
        var normalized = value.Trim().Replace(",,", ",").Replace('/', '\\');
        while (normalized.Contains("\\\\", StringComparison.Ordinal))
            normalized = normalized.Replace("\\\\", "\\", StringComparison.Ordinal);
        return normalized;
    }

    private string 主磁盘占用提示() =>
        T("tools.vmDiskBusy", "仿真运行时不能对其主磁盘启动此操作。请先关闭仿真，或选择其他磁盘。");

    private static string FormatResult(进程结果 result) => string.Format(T("tools.exitCode", "退出码 {0}"), result.退出码) + Environment.NewLine + (string.IsNullOrWhiteSpace(result.输出) ? T("tools.noOutput", "（无输出）") : result.输出);
}
