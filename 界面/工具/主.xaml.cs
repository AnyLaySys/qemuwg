using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.数据;
using QemuWG.服务;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace QemuWG.界面;

public sealed partial class QEMU工具界面 : UserControl
{
    private static readonly HashSet<string> MutatingCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "aio_write", "break", "discard", "flush", "open", "reopen", "remove_break", "resume",
        "truncate", "write", "writev", "zone_append", "zone_close", "zone_finish", "zone_open", "zone_reset"
    };
    private static string T(string key, string fallback) => 语言服务.Current.Get(key, fallback);

    private readonly nint ownerHandle;
    private readonly QEMU安装 install;
    private readonly QEMU工具会话 sessions;
    private readonly QEMU工具服务 tools = new();
    private readonly List<QEMUIO命令> allCommands = [];
    private CancellationTokenSource? ioCancellation;
    private CancellationTokenSource? toolCancellation;
    private bool confirmationPending;

    private bool initialized;

    public QEMU工具界面(nint ownerHandle, QEMU安装 install, QEMU工具会话 sessions)
    {
        InitializeComponent();
        this.ownerHandle = ownerHandle;
        this.install = install;
        this.sessions = sessions;
        sessions.OutputReceived += Sessions_OutputReceived;
        Unloaded += (_, _) =>
        {
            sessions.OutputReceived -= Sessions_OutputReceived;
            ioCancellation?.Cancel();
            toolCancellation?.Cancel();
        };
        Loaded += InitializeOnFirstLoad;
    }

    private async void InitializeOnFirstLoad(object sender, RoutedEventArgs args)
    {
        if (initialized) return;
        initialized = true;
        try { await InitializeAsync(); }
        catch (Exception exception) { 应用日志.Write("QEMU工具界面 initialization failed: " + exception); }
    }

    public ObservableCollection<QEMUIO命令> VisibleCommands { get; } = [];

    private async Task InitializeAsync()
    {
        IoCommandList.ItemsSource = VisibleCommands;
        try
        {
            var discovered = await Task.Run(() => tools.GetQemuIoCommandsAsync(install));
            allCommands.AddRange(discovered);
            ApplyFilter(string.Empty);
        }
        catch (Exception exception) { IoOutputBox.Text = exception.Message; }
    }

    private async void BrowseIoImage_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, ownerHandle);
        picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync();
        if (file is not null) IoImageBox.Text = file.Path;
    }

    private void IoSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) ApplyFilter(sender.Text);
    }

    private void ApplyFilter(string text)
    {
        VisibleCommands.Clear();
        foreach (var command in allCommands.Where(item => item.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || item.Syntax.Contains(text, StringComparison.OrdinalIgnoreCase)))
            VisibleCommands.Add(command);
    }

    private void IoCommandList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IoCommandList.SelectedItem is QEMUIO命令 command) IoCommandBox.Text = command.Name;
        ResetConfirmation();
    }

    private async void RunIoButton_Click(object sender, RoutedEventArgs e)
    {
        var image = IoImageBox.Text.Trim();
        var command = IoCommandBox.Text.Trim();
        if (!File.Exists(image)) { IoOutputBox.Text = T("tools.selectImage", "请选择镜像文件。"); return; }
        if (command.Length == 0) { IoOutputBox.Text = T("tools.selectCommand", "请选择或输入命令。"); return; }
        var name = command.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        if (MutatingCommands.Contains(name) && !confirmationPending)
        {
            confirmationPending = true;
            RunIoButtonText.Text = T("common.confirmExecute", "确认执行");
            IoOutputBox.Text = T("tools.dangerousConfirm", "该 qemu-io 命令可能修改镜像。检查命令后再次执行。");
            return;
        }

        ResetConfirmation();
        SetIoRunning(true);
        ioCancellation = new CancellationTokenSource();
        try { IoOutputBox.Text = FormatResult(await tools.RunQemuIoAsync(install, image, command, BuildIoOptions(), ioCancellation.Token)); }
        catch (OperationCanceledException) { IoOutputBox.Text = T("tools.cancelled", "操作已取消。"); }
        catch (Exception exception) { IoOutputBox.Text = exception.ToString(); }
        finally
        {
            ioCancellation.Dispose();
            ioCancellation = null;
            SetIoRunning(false);
        }
    }

    private IEnumerable<string> BuildIoOptions()
    {
        if (!string.IsNullOrWhiteSpace(IoFormatBox.Text)) { yield return "-f"; yield return IoFormatBox.Text.Trim(); }
        if (IoReadOnlyToggle.IsOn) yield return "-r";
        if (IoSnapshotToggle.IsOn) yield return "-s";
        if (IoNoCacheToggle.IsOn) yield return "-n";
        if (IoCopyOnReadToggle.IsOn) yield return "-C";
        if (IoForceShareToggle.IsOn) yield return "-U";
        if (IoCacheCombo.SelectedItem is string cache) { yield return "-t"; yield return cache; }
        if (IoAioCombo.SelectedItem is string aio) { yield return "-i"; yield return aio; }
        if (IoDiscardCombo.SelectedItem is string discard) { yield return "-d"; yield return discard; }
    }

    private void CancelIoButton_Click(object sender, RoutedEventArgs e) => ioCancellation?.Cancel();
    private void SetIoRunning(bool running)
    {
        RunIoButton.IsEnabled = !running;
        CancelIoButton.Visibility = IoProgress.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        IoProgress.IsActive = running;
    }
    private void ResetConfirmation() { confirmationPending = false; RunIoButtonText.Text = T("tools.run", "运行"); }

    private async void BrowseEdidOutput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedFileName = "display", DefaultFileExtension = ".bin" };
        InitializeWithWindow.Initialize(picker, ownerHandle);
        picker.FileTypeChoices.Add(T("tools.edidFile", "EDID 文件"), [".bin", ".edid"]);
        var file = await picker.PickSaveFileAsync();
        if (file is not null) EdidOutputBox.Text = file.Path;
    }

    private async void GenerateEdidButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(EdidOutputBox.Text)) { EdidResultBox.Text = T("tools.selectOutput", "请选择输出文件。"); return; }
        EdidProgress.IsActive = true;
        EdidProgress.Visibility = Visibility.Visible;
        try
        {
            var result = await tools.GenerateEdidAsync(install, BuildEdidArguments());
            EdidResultBox.Text = result.ExitCode == 0 ? T("tools.generated", "EDID 已生成。") + Environment.NewLine + EdidOutputBox.Text : FormatResult(result);
        }
        catch (Exception exception) { EdidResultBox.Text = exception.ToString(); }
        finally { EdidProgress.IsActive = false; EdidProgress.Visibility = Visibility.Collapsed; }
    }

    private IEnumerable<string> BuildEdidArguments()
    {
        yield return "-o"; yield return EdidOutputBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(EdidVendorBox.Text)) { yield return "-v"; yield return EdidVendorBox.Text.Trim(); }
        if (!string.IsNullOrWhiteSpace(EdidNameBox.Text)) { yield return "-n"; yield return EdidNameBox.Text.Trim(); }
        if (!string.IsNullOrWhiteSpace(EdidSerialBox.Text)) { yield return "-s"; yield return EdidSerialBox.Text.Trim(); }
        foreach (var pair in new[] { ("-d", EdidDpiBox.Value), ("-x", EdidPreferredWidthBox.Value), ("-y", EdidPreferredHeightBox.Value), ("-X", EdidMaximumWidthBox.Value), ("-Y", EdidMaximumHeightBox.Value) })
            if (!double.IsNaN(pair.Value) && pair.Value > 0) { yield return pair.Item1; yield return ((int)pair.Value).ToString(); }
    }

    private void ServiceToolCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ServiceArgumentsBox.PlaceholderText = "--help";

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
        arguments.AddRange(命令行.Split(NbdExtraBox.Text));
        arguments.Add(NbdImageBox.Text.Trim());
        AppendNbdResult(sessions.Start(install, "qemu-nbd.exe", JoinArguments(arguments)));
    }

    private void StopNbd_Click(object sender, RoutedEventArgs e)
    {
        if (sessions.IsRunning("qemu-nbd.exe")) AppendNbdResult(sessions.Stop("qemu-nbd.exe"));
        else NbdOutputBox.Text = T("tools.notRunning", "工具没有运行");
    }

    private void StartStorageDaemon_Click(object sender, RoutedEventArgs e)
    {
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
        AppendStorageResult(sessions.Start(install, "qemu-storage-daemon.exe", JoinArguments(arguments)));
    }

    private void StopStorageDaemon_Click(object sender, RoutedEventArgs e)
    {
        if (sessions.IsRunning("qemu-storage-daemon.exe")) AppendStorageResult(sessions.Stop("qemu-storage-daemon.exe"));
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

    private static string JoinArguments(IEnumerable<string> arguments) => string.Join(' ', arguments.Select(命令行.Quote));
    private void AppendNbdResult(操作结果 result) => NbdOutputBox.Text += (NbdOutputBox.Text.Length == 0 ? string.Empty : Environment.NewLine) + ResultText(result);
    private void AppendStorageResult(操作结果 result) => StorageOutputBox.Text += (StorageOutputBox.Text.Length == 0 ? string.Empty : Environment.NewLine) + ResultText(result);
    private static string ResultText(操作结果 result) => result.Succeeded ? result.Message : string.Join(Environment.NewLine, new[] { result.Message, result.Detail }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private async void RunToolOnce_Click(object sender, RoutedEventArgs e)
    {
        RunToolOnceButton.IsEnabled = false;
        toolCancellation = new CancellationTokenSource();
        try { ServiceOutputBox.Text = FormatResult(await tools.RunToolAsync(install, SelectedTool, ServiceArgumentsBox.Text, toolCancellation.Token)); }
        catch (OperationCanceledException) { ServiceOutputBox.Text = T("tools.cancelled", "操作已取消。"); }
        catch (Exception exception) { ServiceOutputBox.Text = exception.ToString(); }
        finally { toolCancellation.Dispose(); toolCancellation = null; RunToolOnceButton.IsEnabled = true; }
    }

    private void StartTool_Click(object sender, RoutedEventArgs e) => AppendResult(sessions.Start(install, SelectedTool, ServiceArgumentsBox.Text));
    private void StopTool_Click(object sender, RoutedEventArgs e)
    {
        toolCancellation?.Cancel();
        if (sessions.IsRunning(SelectedTool)) AppendResult(sessions.Stop(SelectedTool));
    }
    private void ClearToolOutput_Click(object sender, RoutedEventArgs e) => ServiceOutputBox.Text = string.Empty;
    private string SelectedTool => ServiceToolCombo.SelectedItem?.ToString() ?? "qemu-nbd.exe";

    private void Sessions_OutputReceived(object? sender, 工具输出事件 e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (string.Equals(e.Tool, "qemu-nbd.exe", StringComparison.OrdinalIgnoreCase))
            NbdOutputBox.Text += (NbdOutputBox.Text.Length == 0 ? string.Empty : Environment.NewLine) + e.Text;
        if (string.Equals(e.Tool, "qemu-storage-daemon.exe", StringComparison.OrdinalIgnoreCase))
            StorageOutputBox.Text += (StorageOutputBox.Text.Length == 0 ? string.Empty : Environment.NewLine) + e.Text;
        if (string.Equals(e.Tool, SelectedTool, StringComparison.OrdinalIgnoreCase)) AppendOutput(e.Text);
    });
    private void AppendResult(操作结果 result) => AppendOutput(result.Succeeded ? result.Message : string.Join(Environment.NewLine, new[] { result.Message, result.Detail }.Where(value => !string.IsNullOrWhiteSpace(value))));
    private void AppendOutput(string text) => ServiceOutputBox.Text += (ServiceOutputBox.Text.Length == 0 ? string.Empty : Environment.NewLine) + text;
    private static string FormatResult(进程结果 result) => string.Format(T("tools.exitCode", "退出码 {0}"), result.ExitCode) + Environment.NewLine + (string.IsNullOrWhiteSpace(result.Output) ? T("tools.noOutput", "（无输出）") : result.Output);
}

