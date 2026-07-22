using Microsoft.UI.Xaml;
using QemuWG.数据;
using QemuWG.服务;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace QemuWG.界面;

public sealed partial class QEMU附加功能
{
    private async void BrowseNbdImage_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, ownerHandle);
        picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync();
        if (file is not null) NbdImageBox.Text = file.Path;
    }

    private void StartNbd_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(NbdImageBox.Text)) { NbdOutputBox.Text = T("tools.selectImage", "请选择镜像文件。"); return; }
        if (仿真正在占用主磁盘(NbdImageBox.Text)) { NbdOutputBox.Text = 主磁盘占用提示(); return; }
        var arguments = new List<string>
        {
            "--bind", NbdBindBox.Text.Trim(), "--port", ((int)NbdPortBox.Value).ToString(), "--shared", ((int)NbdSharedBox.Value).ToString()
        };
        AddOption(arguments, "--format", NbdFormatBox.Text);
        AddOption(arguments, "--export-name", NbdExportNameBox.Text);
        AddOption(arguments, "--description", NbdDescriptionBox.Text);
        AddOption(arguments, "--cache", NbdCacheCombo.SelectedItem?.ToString());
        AddOption(arguments, "--aio", NbdAioCombo.SelectedItem?.ToString());
        AddOption(arguments, "--discard", NbdDiscardCombo.SelectedItem?.ToString());
        if (NbdReadOnlyToggle.IsOn) arguments.Add("--read-only");
        if (NbdSnapshotToggle.IsOn) arguments.Add("--snapshot");
        if (NbdPersistentToggle.IsOn) arguments.Add("--persistent");
        if (NbdAllocationDepthToggle.IsOn) arguments.Add("--allocation-depth");
        if (NbdVerboseToggle.IsOn) arguments.Add("--verbose");
        arguments.AddRange(命令行.分割(NbdExtraBox.Text));
        arguments.Add(NbdImageBox.Text.Trim());
        AppendNbdResult(sessions.启动(install, machine.Id, "qemu-nbd.exe", JoinArguments(arguments)));
    }

    private void StopNbd_Click(object sender, RoutedEventArgs e)
    {
        if (sessions.正在运行(machine.Id, "qemu-nbd.exe")) AppendNbdResult(sessions.停止(machine.Id, "qemu-nbd.exe"));
        else NbdOutputBox.Text = T("tools.notRunning", "工具没有运行");
    }

    private void AppendNbdResult(操作结果 result) => NbdOutputBox.Text += (NbdOutputBox.Text.Length == 0 ? string.Empty : Environment.NewLine) + ResultText(result);
}
