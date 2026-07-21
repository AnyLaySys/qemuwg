using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.数据;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace QemuWG.界面;

public sealed partial class QEMU附加功能
{
    private static readonly HashSet<string> MutatingCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "aio_write", "break", "discard", "flush", "open", "reopen", "remove_break", "resume",
        "truncate", "write", "writev", "zone_append", "zone_close", "zone_finish", "zone_open", "zone_reset"
    };

    private readonly List<QEMUIO命令> allCommands = [];
    private CancellationTokenSource? ioCancellation;
    private bool confirmationPending;

    public ObservableCollection<QEMUIO命令> VisibleCommands { get; } = [];

    private async Task InitializeImageIoAsync()
    {
        IoCommandList.ItemsSource = VisibleCommands;
        try
        {
            var discovered = await Task.Run(() => tools.获取QEMU输入输出命令(install));
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
        try { IoOutputBox.Text = FormatResult(await tools.运行QEMU输入输出(install, image, command, BuildIoOptions(), ioCancellation.Token)); }
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
}
