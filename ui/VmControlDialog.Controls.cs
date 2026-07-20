using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.data;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace QemuWG.ui;

public sealed partial class VmControlDialog
{
    private async void PauseButton_Click(object sender, RoutedEventArgs e) => await ExecuteControlAsync("stop");
    private async void ResumeButton_Click(object sender, RoutedEventArgs e) => await ExecuteControlAsync("cont");
    private async void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var result = await ExecuteControlAsync("system_reset");
        if (!result.Succeeded) return;
        await Task.Delay(900);
        if (sessions.HasQmpSession(machine)) return;

        var restart = sessions.Start(install, machine);
        ControlOutputBox.Text = restart.Succeeded
            ? T("qmp.resetRestarted", "QEMU 在重置时退出，虚拟机已自动重新启动。")
            : T("qmp.resetRestartFailed", "QEMU 在重置时退出，自动重新启动失败：") + Environment.NewLine + restart.Message + Environment.NewLine + restart.Detail;
        if (restart.Succeeded) await Task.Delay(700);
        await RefreshSummaryAsync();
    }
    private async void PowerdownButton_Click(object sender, RoutedEventArgs e)
    {
        var result = await ExecuteControlAsync("system_powerdown");
        if (!result.Succeeded) return;
        ControlOutputBox.Text = T("qmp.shutdownWaiting", "关机请求已发送，正在等待来宾系统响应…");
        if (await sessions.WaitForExitAsync(machine, TimeSpan.FromSeconds(12)))
            ControlOutputBox.Text = T("qmp.shutdownComplete", "虚拟机已关机。");
        else
            ControlOutputBox.Text = T("qmp.shutdownNoResponse", "来宾系统没有响应关机请求。可以继续等待，或使用强制停止。 ");
    }

    private async void ForceStopControl_Click(object sender, RoutedEventArgs e)
    {
        if (!forceStopPending)
        {
            forceStopPending = true;
            ForceStopControlText.Text = T("qmp.confirmForceStop", "确认强制停止");
            ControlOutputBox.Text = T("dialog.forceStopMessage", "未保存的数据可能丢失。");
            return;
        }
        var result = sessions.ForceStop(machine);
        ControlOutputBox.Text = result.Succeeded ? result.Message : result.Message + Environment.NewLine + result.Detail;
        if (result.Succeeded)
        {
            await sessions.WaitForExitAsync(machine, TimeSpan.FromSeconds(3));
            Hide();
        }
    }
    private async void NmiButton_Click(object sender, RoutedEventArgs e) => await ExecuteControlAsync("inject-nmi");
    private async void CtrlAltDelete_Click(object sender, RoutedEventArgs e) => await SendKeysAsync(["ctrl", "alt", "delete"]);
    private async void CtrlShiftEscape_Click(object sender, RoutedEventArgs e) => await SendKeysAsync(["ctrl", "shift", "esc"]);
    private async void AltF4_Click(object sender, RoutedEventArgs e) => await SendKeysAsync(["alt", "f4"]);
    private async void WinR_Click(object sender, RoutedEventArgs e) => await SendKeysAsync(["meta_l", "r"]);

    private async void SendCustomKey_Click(object sender, RoutedEventArgs e)
    {
        var keys = new List<string>();
        if (CtrlKeyBox.IsChecked == true) keys.Add("ctrl");
        if (AltKeyBox.IsChecked == true) keys.Add("alt");
        if (ShiftKeyBox.IsChecked == true) keys.Add("shift");
        if (MetaKeyBox.IsChecked == true) keys.Add("meta_l");
        var selected = KeyCombo.SelectedItem?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(selected)) keys.Add(selected.Trim().ToLowerInvariant());
        if (keys.Count > 0) await SendKeysAsync(keys);
    }

    private Task SendKeysAsync(IReadOnlyList<string> keys)
    {
        var arguments = new JsonObject
        {
            ["keys"] = new JsonArray(keys.Select(key => (JsonNode)new JsonObject
            {
                ["type"] = "qcode",
                ["data"] = key
            }).ToArray()),
            ["hold-time"] = double.IsNaN(HoldTimeBox.Value) ? 100 : (int)HoldTimeBox.Value
        };
        return ExecuteControlAsync("send-key", arguments.ToJsonString());
    }

    private async void ScreenshotButton_Click(object sender, RoutedEventArgs e)
    {
        var directory = Path.Combine(machine.DirPath, "screenshots");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmss}.png");
        var arguments = new JsonObject { ["filename"] = path, ["format"] = "png" };
        var result = await ExecuteControlAsync("screendump", arguments.ToJsonString());
        if (result.Succeeded) ScreenshotPathText.Text = path;
    }

    private async Task<QmpResult> ExecuteControlAsync(string command, string arguments = "")
    {
        forceStopPending = false;
        ForceStopControlText.Text = T("main.forceStop", "强制停止");
        ControlBusyRing.IsActive = true;
        ControlBusyRing.Visibility = Visibility.Visible;
        try
        {
            var result = await sessions.ExecuteQmpAsync(machine, command, arguments);
            ControlOutputBox.Text = result.Succeeded
                ? string.Format(T("qmp.commandSucceeded", "{0} 已执行。"), ControlName(command)) + (result.Output is "{}" or "null" ? string.Empty : Environment.NewLine + result.Output)
                : result.Output;
            await RefreshSummaryAsync();
            return result;
        }
        finally
        {
            ControlBusyRing.IsActive = false;
            ControlBusyRing.Visibility = Visibility.Collapsed;
        }
    }

    private static string ControlName(string command) => command switch
    {
        "stop" => T("qmp.pause", "暂停"),
        "cont" => T("qmp.resume", "继续"),
        "system_reset" => T("qmp.reset", "重置"),
        "system_powerdown" => T("qmp.powerdown", "正常关机"),
        "inject-nmi" => T("qmp.injectNmi", "注入 NMI"),
        "send-key" => T("qmp.keyboard", "键盘输入"),
        "screendump" => T("qmp.screenshot", "截取虚拟机画面"),
        _ => command
    };

    private async void BrowseMedia_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, ownerHandle);
        picker.FileTypeFilter.Add(".iso");
        picker.FileTypeFilter.Add(".img");
        picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync();
        if (file is not null) MediaPathBox.Text = file.Path;
    }

    private async void MountMedia_Click(object sender, RoutedEventArgs e)
    {
        var path = MediaPathBox.Text.Trim();
        if (!File.Exists(path)) { ControlOutputBox.Text = T("qmp.selectMedia", "请选择有效的光盘镜像。"); return; }
        var arguments = new JsonObject { ["id"] = "install-media", ["filename"] = path, ["read-only-mode"] = "retain" };
        var result = await ExecuteControlAsync("blockdev-change-medium", arguments.ToJsonString());
        if (result.Succeeded) machine.IsoPath = path;
    }

    private async void EjectMedia_Click(object sender, RoutedEventArgs e) =>
        await ExecuteControlAsync("eject", new JsonObject { ["device"] = "install-media", ["force"] = false }.ToJsonString());

    private async void ApplyBalloon_Click(object sender, RoutedEventArgs e)
    {
        if (double.IsNaN(BalloonMemoryBox.Value)) return;
        await ExecuteControlAsync("balloon", new JsonObject { ["value"] = (long)BalloonMemoryBox.Value * 1024 * 1024 }.ToJsonString());
    }

    private async void SaveSnapshot_Click(object sender, RoutedEventArgs e) => await RunSnapshotCommandAsync("savevm");
    private async void LoadSnapshot_Click(object sender, RoutedEventArgs e) => await RunSnapshotCommandAsync("loadvm");
    private async void DeleteSnapshot_Click(object sender, RoutedEventArgs e) => await RunSnapshotCommandAsync("delvm");

    private async Task RunSnapshotCommandAsync(string operation)
    {
        var name = SnapshotNameBox.Text.Trim();
        if (name.Length == 0 || name.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            ControlOutputBox.Text = T("qmp.snapshotNameInvalid", "快照名称只能包含字母、数字、点、连字符和下划线。");
            return;
        }
        await ExecuteControlAsync("human-monitor-command", new JsonObject { ["command-line"] = $"{operation} {name}" }.ToJsonString());
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
}

