using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.data;
using QemuWG.svc;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace QemuWG.ui;

public sealed partial class DiskMgrDialog : ContentDialog
{
    private static string T(string key, string fallback) => LocaleSvc.Current.Get(key, fallback);

    private readonly nint ownerHandle;
    private readonly QemuInstall install;
    private readonly VmCfg machine;
    private readonly QemuImgSvc qemuImgSvc = new();
    private readonly Dictionary<string, FrameworkElement> inputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FrameworkElement> fieldContainers = new(StringComparer.OrdinalIgnoreCase);
    private DiskCmdDef? selectedCmd;
    private CancellationTokenSource? runCancellation;
    private bool awaitingConfirmation;

    public DiskMgrDialog(nint ownerHandle, QemuInstall install, VmCfg machine)
    {
        InitializeComponent();
        DialogLayout.EnableAdaptiveSizing(this);
        this.ownerHandle = ownerHandle;
        this.install = install;
        this.machine = machine;
        Title = T("disk.title", "磁盘管理");
        DiskNameText.Text = string.IsNullOrWhiteSpace(machine.DiskPath) ? T("disk.workspace", "磁盘工作区") : Path.GetFileName(machine.DiskPath);
        CommandList.ItemsSource = QemuImgCmdCatalog.All;
        Loaded += async (_, _) =>
        {
            CommandList.SelectedIndex = 0;
            await RefreshDiskInfoAsync();
        };
    }

    private void CommandList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        selectedCmd = CommandList.SelectedItem as DiskCmdDef;
        if (selectedCmd is null) return;
        BuildForm(selectedCmd);
    }

    private async void ExecuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedCmd is null) return;
        if (machine.IsRunning && selectedCmd.CanWrite)
        {
            SafetyInfo.Message = T("disk.closeVmFirst", "请先关闭虚拟机。");
            SafetyInfo.IsOpen = true;
            return;
        }

        IReadOnlyDictionary<string, string> values = ReadValues();
        try
        {
            CommandPreviewBox.Text = qemuImgSvc.BuildArgs(selectedCmd, values).Preview;
        }
        catch (InvalidOperationException exception)
        {
            SafetyInfo.Message = exception.Message;
            SafetyInfo.IsOpen = true;
            return;
        }

        if (selectedCmd.CanWrite && !awaitingConfirmation)
        {
            awaitingConfirmation = true;
            ExecuteButtonText.Text = T("common.confirmExecute", "确认执行");
            SafetyInfo.Message = T("disk.destructiveConfirm", "该命令可能修改或覆盖磁盘数据。请检查命令预览后再次执行。");
            SafetyInfo.IsOpen = true;
            return;
        }

        await RunCommandAsync(selectedCmd, values);
    }

    private async Task RunCommandAsync(DiskCmdDef command, IReadOnlyDictionary<string, string> values)
    {
        awaitingConfirmation = false;
        ExecuteButtonText.Text = T("common.execute", "执行");
        SafetyInfo.IsOpen = false;
        SetRunningState(true);
        runCancellation = new CancellationTokenSource();
        try
        {
            var result = await qemuImgSvc.ExecuteAsync(install, command, values, runCancellation.Token);
            ExitCodeText.Text = string.Format(T("disk.exitCode", "退出码 {0}"), result.ExitCode);
            OutputBox.Text = string.IsNullOrWhiteSpace(result.Output) ? T("disk.noOutput", "（无输出）") : result.Output;
            await RefreshDiskInfoAsync();
        }
        catch (OperationCanceledException)
        {
            ExitCodeText.Text = T("disk.stateCancelled", "已取消");
            OutputBox.Text = T("disk.cancelled", "操作已取消。");
        }
        catch (Exception exception)
        {
            ExitCodeText.Text = T("disk.stateFailed", "失败");
            OutputBox.Text = exception.ToString();
        }
        finally
        {
            runCancellation?.Dispose();
            runCancellation = null;
            SetRunningState(false);
        }
    }

    private void CancelRunButton_Click(object sender, RoutedEventArgs e) => runCancellation?.Cancel();

    private void SetRunningState(bool running)
    {
        CommandList.IsEnabled = !running;
        FormGrid.IsHitTestVisible = !running;
        ExecuteButton.IsEnabled = !running && !(machine.IsRunning && selectedCmd?.CanWrite == true);
        CancelRunButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        RunProgress.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task RefreshDiskInfoAsync()
    {
        var info = await qemuImgSvc.GetInfoAsync(install, machine.DiskPath);
        if (info is null)
        {
            DiskInfoText.Text = string.IsNullOrWhiteSpace(machine.DiskPath)
                ? $"{install.Version} · {install.ImgToolPath}"
                : machine.DiskPath;
            return;
        }
        var backing = string.IsNullOrWhiteSpace(info.BackingFile)
            ? string.Empty
            : " · " + string.Format(T("disk.backingFile", "后备文件 {0}"), info.BackingFile);
        DiskInfoText.Text = string.Join(" · ",
            info.Format,
            string.Format(T("disk.virtualSize", "虚拟容量 {0}"), FormatBytes(info.VirtualSize)),
            string.Format(T("disk.actualSize", "实际占用 {0}"), FormatBytes(info.ActualSize))) + backing;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

}


