using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.data;
using QemuWG.svc;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace QemuWG.ui;

public sealed partial class VmControlDialog : ContentDialog
{
    private readonly QemuSessionMgr sessions;
    private readonly VmCfg machine;
    private readonly nint ownerHandle;
    private readonly List<QmpCmdInfo> allCommands = [];
    private static string T(string key, string fallback) => LocaleSvc.Current.Get(key, fallback);

    public VmControlDialog(nint ownerHandle, QemuSessionMgr sessions, VmCfg machine)
    {
        InitializeComponent();
        DialogLayout.EnableAdaptiveSizing(this);
        this.sessions = sessions;
        this.machine = machine;
        this.ownerHandle = ownerHandle;
        Title = T("qmp.title", "QMP 控制台");
        Loaded += async (_, _) => await InitializeAsync();
    }

    public ObservableCollection<QmpCmdInfo> VisibleCommands { get; } = [];

    private async Task InitializeAsync()
    {
        KeyCombo.ItemsSource = new[]
        {
            "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z",
            "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "esc", "tab", "ret", "spc", "backspace", "delete", "insert", "home", "end", "pgup", "pgdn",
            "up", "down", "left", "right", "f1", "f2", "f3", "f4", "f5", "f6", "f7", "f8", "f9", "f10", "f11", "f12"
        };
        BalloonMemoryBox.Value = machine.MemoryMb;
        MediaPathBox.Text = machine.IsoPath;
        CommandList.ItemsSource = VisibleCommands;
        OutputBox.Text = T("qmp.loading", "正在读取 QMP Schema…");
        await RefreshSummaryAsync();

        var commandsResult = await sessions.ExecuteQmpAsync(machine, "query-commands");
        var schemaResult = await sessions.ExecuteQmpAsync(machine, "query-qmp-schema");
        if (!commandsResult.Succeeded)
        {
            OutputBox.Text = commandsResult.Output;
            return;
        }

        var hints = schemaResult.Succeeded ? ParseSchemaHints(schemaResult.Output) : new Dictionary<string, string>();
        using var document = JsonDocument.Parse(commandsResult.Output);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString() ?? string.Empty;
            allCommands.Add(new QmpCmdInfo { Name = name, ArgumentsHint = hints.GetValueOrDefault(name, "{}") });
        }
        allCommands.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        ApplyFilter(string.Empty);
        OutputBox.Text = string.Empty;
        CommandList.SelectedIndex = allCommands.FindIndex(command => command.Name == "query-status");
    }

    private async Task RefreshSummaryAsync()
    {
        var unknown = T("qmp.unknown", "未知");
        StatusValue.Text = CpuValue.Text = MemoryValue.Text = BlockValue.Text = unknown;
        var statusTask = sessions.ExecuteQmpAsync(machine, "query-status");
        var cpuTask = sessions.ExecuteQmpAsync(machine, "query-cpus-fast");
        var memoryTask = sessions.ExecuteQmpAsync(machine, "query-memory-size-summary");
        var blockTask = sessions.ExecuteQmpAsync(machine, "query-block");
        await Task.WhenAll(statusTask, cpuTask, memoryTask, blockTask);

        if (statusTask.Result.Succeeded)
        {
            using var status = JsonDocument.Parse(statusTask.Result.Output);
            StatusValue.Text = status.RootElement.TryGetProperty("status", out var value) ? value.GetString() ?? unknown : unknown;
        }
        if (cpuTask.Result.Succeeded)
        {
            using var cpus = JsonDocument.Parse(cpuTask.Result.Output);
            CpuValue.Text = cpus.RootElement.GetArrayLength().ToString();
        }
        if (memoryTask.Result.Succeeded)
        {
            using var memory = JsonDocument.Parse(memoryTask.Result.Output);
            var root = memory.RootElement;
            var total = (root.TryGetProperty("base-memory", out var baseMemory) ? baseMemory.GetInt64() : 0)
                        + (root.TryGetProperty("plugged-memory", out var plugged) ? plugged.GetInt64() : 0);
            MemoryValue.Text = FormatBytes(total);
        }
        if (blockTask.Result.Succeeded)
        {
            using var blocks = JsonDocument.Parse(blockTask.Result.Output);
            BlockValue.Text = blocks.RootElement.GetArrayLength().ToString();
        }
    }

    private static Dictionary<string, string> ParseSchemaHints(string json)
    {
        using var document = JsonDocument.Parse(json);
        var entries = document.RootElement.EnumerateArray().Select(element => element.Clone()).ToList();
        var objects = entries.Where(element => GetMetaType(element) == "object" && element.TryGetProperty("name", out _))
            .ToDictionary(element => element.GetProperty("name").GetString() ?? string.Empty, StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var command in entries.Where(element => GetMetaType(element) == "command"))
        {
            var name = command.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
            var argumentType = command.TryGetProperty("arg-type", out var typeValue) ? typeValue.GetString() : null;
            if (name is null || argumentType is null || !objects.TryGetValue(argumentType, out var argumentObject)) continue;
            var members = argumentObject.TryGetProperty("members", out var membersValue)
                ? membersValue.EnumerateArray().Select(member =>
                {
                    var memberName = member.GetProperty("name").GetString() ?? string.Empty;
                    var memberType = member.GetProperty("type").GetString() ?? "any";
                    var optional = member.TryGetProperty("default", out _) ? "?" : string.Empty;
                    return $"{memberName}{optional}: {memberType}";
                })
                : [];
            result[name] = "{ " + string.Join(", ", members) + " }";
        }
        return result;
    }

    private static string GetMetaType(JsonElement element) =>
        element.TryGetProperty("meta-type", out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) ApplyFilter(sender.Text);
    }

    private void ApplyFilter(string text)
    {
        VisibleCommands.Clear();
        foreach (var command in allCommands.Where(command => command.Name.Contains(text, StringComparison.OrdinalIgnoreCase)))
            VisibleCommands.Add(command);
        CommandCountText.Text = string.Format(T("qmp.commandCount", "{0} 个命令"), VisibleCommands.Count);
    }

    private void CommandList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CommandList.SelectedItem is not QmpCmdInfo command) return;
        CommandBox.Text = command.Name;
        SchemaHintText.Text = command.ArgumentsHint;
        ArgumentsBox.Text = "{}";
    }

    private void CommandBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SchemaHintText.Text = allCommands.FirstOrDefault(command => command.Name == CommandBox.Text)?.ArgumentsHint ?? "{}";
    }

    private async void ExecuteButton_Click(object sender, RoutedEventArgs e)
    {
        var command = CommandBox.Text.Trim();
        if (command.Length == 0) return;
        BusyRing.IsActive = true;
        try
        {
            var arguments = ArgumentsBox.Text.Trim();
            var result = await sessions.ExecuteQmpAsync(machine, command, arguments is "" or "{}" ? string.Empty : arguments);
            OutputBox.Text = result.Output;
            await RefreshSummaryAsync();
        }
        finally
        {
            BusyRing.IsActive = false;
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshSummaryAsync();

    private async void PauseButton_Click(object sender, RoutedEventArgs e) => await ExecuteControlAsync("stop");
    private async void ResumeButton_Click(object sender, RoutedEventArgs e) => await ExecuteControlAsync("cont");
    private async void ResetButton_Click(object sender, RoutedEventArgs e) => await ExecuteControlAsync("system_reset");
    private async void PowerdownButton_Click(object sender, RoutedEventArgs e) => await ExecuteControlAsync("system_powerdown");
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
        ControlBusyRing.IsActive = true;
        ControlBusyRing.Visibility = Visibility.Visible;
        try
        {
            var result = await sessions.ExecuteQmpAsync(machine, command, arguments);
            ControlOutputBox.Text = result.Succeeded
                ? string.Format(T("qmp.commandSucceeded", "{0} 已执行。"), command) + (result.Output is "{}" or "null" ? string.Empty : Environment.NewLine + result.Output)
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



