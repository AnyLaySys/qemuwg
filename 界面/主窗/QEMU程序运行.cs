using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.数据;
using QemuWG.服务;

namespace QemuWG.界面;

public sealed partial class QEMU附加功能
{
    private CancellationTokenSource? toolCancellation;

    private void ServiceToolCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ServiceArgumentsBox.PlaceholderText = "--help";

    private async void RunToolOnce_Click(object sender, RoutedEventArgs e)
    {
        RunToolOnceButton.IsEnabled = false;
        toolCancellation = new CancellationTokenSource();
        try { ServiceOutputBox.Text = FormatResult(await tools.运行工具(install, SelectedTool, ServiceArgumentsBox.Text, toolCancellation.Token)); }
        catch (OperationCanceledException) { ServiceOutputBox.Text = T("tools.cancelled", "操作已取消。"); }
        catch (Exception exception) { ServiceOutputBox.Text = exception.ToString(); }
        finally { toolCancellation.Dispose(); toolCancellation = null; RunToolOnceButton.IsEnabled = true; }
    }

    private void StartTool_Click(object sender, RoutedEventArgs e) => AppendResult(sessions.启动(install, SelectedTool, ServiceArgumentsBox.Text));

    private void StopTool_Click(object sender, RoutedEventArgs e)
    {
        toolCancellation?.Cancel();
        if (sessions.正在运行(SelectedTool)) AppendResult(sessions.停止(SelectedTool));
    }

    private void ClearToolOutput_Click(object sender, RoutedEventArgs e) => ServiceOutputBox.Text = string.Empty;
    private string SelectedTool => ServiceToolCombo.SelectedItem?.ToString() ?? "qemu-nbd.exe";
    private void AppendResult(操作结果 result) => AppendOutput(result.Succeeded ? result.Message : string.Join(Environment.NewLine, new[] { result.Message, result.Detail }.Where(value => !string.IsNullOrWhiteSpace(value))));
    private void AppendOutput(string text) => ServiceOutputBox.Text += (ServiceOutputBox.Text.Length == 0 ? string.Empty : Environment.NewLine) + text;
}
