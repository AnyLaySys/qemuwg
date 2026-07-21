using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.数据;
using QemuWG.服务;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace QemuWG.界面;

public sealed partial class 虚拟机控制 : ContentDialog
{
    private readonly QEMU会话 sessions;
    private readonly QEMU安装 install;
    private readonly 虚拟机配置 machine;
    private readonly nint ownerHandle;
    private readonly List<QMP命令> allCommands = [];
    private bool forceStopPending;
    private static string T(string key, string fallback) => 语言服务.Current.Get(key, fallback);

    public 虚拟机控制(nint ownerHandle, QEMU安装 install, QEMU会话 sessions, 虚拟机配置 machine)
    {
        InitializeComponent();
        对话框布局.EnableAdaptiveSizing(this);
        this.install = install;
        this.sessions = sessions;
        this.machine = machine;
        this.ownerHandle = ownerHandle;
        Title = T("qmp.title", "QMP 控制台");
        Loaded += async (_, _) => await InitializeSafelyAsync();
    }

    public ObservableCollection<QMP命令> VisibleCommands { get; } = [];

    private async Task InitializeSafelyAsync()
    {
        try
        {
            await InitializeAsync();
        }
        catch (Exception exception)
        {
            应用日志.Write("虚拟机控制 initialization failed: " + exception);
            OutputBox.Text = string.Format(T("qmp.openFailed", "无法打开 QMP 控制台：{0}"), exception.Message);
        }
    }

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
            allCommands.Add(new QMP命令 { Name = name, ArgumentsHint = hints.GetValueOrDefault(name, "{}") });
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
            StatusValue.Text = status.RootElement.TryGetProperty("status", out var value)
                ? LocalizeRunState(value.GetString() ?? string.Empty, unknown)
                : unknown;
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

    private static string LocalizeRunState(string state, string unknown) => state switch
    {
        "running" => T("qmp.state.running", "运行中"),
        "paused" => T("qmp.state.paused", "已暂停"),
        "shutdown" => T("qmp.state.shutdown", "正在关机"),
        "prelaunch" => T("qmp.state.prelaunch", "准备启动"),
        "suspended" => T("qmp.state.suspended", "已挂起"),
        "internal-error" => T("qmp.state.internalError", "内部错误"),
        "io-error" => T("qmp.state.ioError", "I/O 错误"),
        "guest-panicked" => T("qmp.state.guestPanicked", "来宾系统崩溃"),
        "" => unknown,
        _ => state
    };

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
        if (CommandList.SelectedItem is not QMP命令 command) return;
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

}
