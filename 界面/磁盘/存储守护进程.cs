using QemuWG.数据;
using QemuWG.服务;

namespace QemuWG.界面;

public sealed partial class QEMU附加功能
{
    private void StartStorageDaemon_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (参数引用主磁盘(
                StorageBlockdevBox.Text,
                StorageNbdServerBox.Text,
                StorageExportBox.Text,
                StorageMonitorBox.Text,
                StorageChardevBox.Text,
                StorageObjectsBox.Text,
                StoragePidFileBox.Text))
        {
            StorageOutputBox.Text = 主磁盘占用提示();
            return;
        }
        var arguments = new List<string>();
        AddRepeated(arguments, "--blockdev", StorageBlockdevBox.Text);
        AddOption(arguments, "--nbd-server", StorageNbdServerBox.Text);
        AddRepeated(arguments, "--export", StorageExportBox.Text);
        AddOption(arguments, "--monitor", StorageMonitorBox.Text);
        AddRepeated(arguments, "--chardev", StorageChardevBox.Text);
        AddRepeated(arguments, "--object", StorageObjectsBox.Text);
        AddOption(arguments, "--pidfile", StoragePidFileBox.Text);
        if (!arguments.Contains("--blockdev") || !arguments.Contains("--export"))
        {
            StorageOutputBox.Text = T("tools.storageRequired", "存储守护进程至少需要一个块设备和一个导出定义。");
            return;
        }
        AppendStorageResult(sessions.启动(install, machine.Id, "qemu-storage-daemon.exe", JoinArguments(arguments)));
    }

    private void StopStorageDaemon_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sessions.正在运行(machine.Id, "qemu-storage-daemon.exe")) AppendStorageResult(sessions.停止(machine.Id, "qemu-storage-daemon.exe"));
        else StorageOutputBox.Text = T("tools.notRunning", "工具没有运行");
    }

    private static void AddOption(ICollection<string> arguments, string option, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        arguments.Add(option);
        arguments.Add(value.Trim());
    }

    private static void AddRepeated(ICollection<string> arguments, string option, string? values)
    {
        if (string.IsNullOrWhiteSpace(values)) return;
        foreach (var value in values.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) AddOption(arguments, option, value);
    }

    private static string JoinArguments(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(命令行.引用));
    private void AppendStorageResult(操作结果 result) => StorageOutputBox.Text += (StorageOutputBox.Text.Length == 0 ? string.Empty : Environment.NewLine) + ResultText(result);
    private static string ResultText(操作结果 result) => result.Succeeded ? result.Message : string.Join(Environment.NewLine, new[] { result.Message, result.Detail }.Where(value => !string.IsNullOrWhiteSpace(value)));
}
