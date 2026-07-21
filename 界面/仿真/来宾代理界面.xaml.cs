using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.数据;
using QemuWG.服务;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace QemuWG.界面;

public sealed partial class 来宾代理界面 : ContentDialog
{
    private const int FileChunkSize = 768 * 1024;
    private static readonly HashSet<string> ConfirmedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "guest-shutdown", "guest-suspend-disk", "guest-suspend-ram", "guest-suspend-hybrid",
        "guest-fsfreeze-freeze", "guest-fsfreeze-freeze-list", "guest-set-time", "guest-exec",
        "guest-file-write", "guest-file-flush", "guest-file-close", "guest-ssh-add-authorized-keys",
        "guest-ssh-remove-authorized-keys"
    };
    private static string T(string key, string fallback) => 语言服务.当前.获取(key, fallback);

    private readonly nint ownerHandle;
    private readonly QEMU会话 sessions;
    private readonly 虚拟机配置 machine;
    private readonly List<来宾代理命令> commands = [];
    private bool confirmationPending;

    public 来宾代理界面(nint ownerHandle, QEMU会话 sessions, 虚拟机配置 machine)
    {
        InitializeComponent();
        对话框布局.EnableAdaptiveSizing(this);
        this.ownerHandle = ownerHandle;
        this.sessions = sessions;
        this.machine = machine;
        Title = T("guestAgent.title", "来宾系统管理");
        Loaded += async (_, _) => await InitializeAsync();
    }

    public ObservableCollection<来宾代理命令> VisibleCommands { get; } = [];

    private async Task InitializeAsync()
    {
        CommandList.ItemsSource = VisibleCommands;
        ConnectionInfo.Message = T("guestAgent.connecting", "正在连接虚拟机内的 Guest Agent…");
        var result = await sessions.执行来宾代理(machine, "guest-info");
        if (!result.Succeeded)
        {
            ConnectionInfo.Severity = InfoBarSeverity.Warning;
            ConnectionInfo.Message = result.Output;
            OutputBox.Text = T("guestAgent.installHint", "请在虚拟机内安装并启动 QEMU Guest Agent；虚拟串口通道已由 QemuWG 自动配置。");
            return;
        }

        commands.AddRange(ParseCommands(result.Output));
        commands.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        ApplyFilter(string.Empty);
        ConnectionInfo.Severity = InfoBarSeverity.Success;
        ConnectionInfo.Message = string.Format(T("guestAgent.connected", "已连接，Guest Agent 提供 {0} 个命令。"), commands.Count);
        CommandList.SelectedIndex = Math.Max(0, commands.FindIndex(item => item.Name == "guest-get-osinfo"));
        await RefreshOverviewAsync();
    }

    private static IEnumerable<来宾代理命令> ParseCommands(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("supported_commands", out var list)) yield break;
        foreach (var item in list.EnumerateArray())
            yield return new 来宾代理命令
            {
                Name = item.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                Enabled = !item.TryGetProperty("enabled", out var enabled) || enabled.GetBoolean(),
                SuccessResponse = !item.TryGetProperty("success-response", out var response) || response.GetBoolean()
            };
    }

    private bool Supports(string command) => commands.Any(item => item.Enabled && string.Equals(item.Name, command, StringComparison.OrdinalIgnoreCase));

    private async Task RefreshOverviewAsync()
    {
        var unavailable = T("guestAgent.unavailableValue", "不可用");
        var osTask = QueryIfSupportedAsync("guest-get-osinfo");
        var usersTask = QueryIfSupportedAsync("guest-get-users");
        var networkTask = QueryIfSupportedAsync("guest-network-get-interfaces");
        var disksTask = QueryIfSupportedAsync("guest-get-disks");
        var fsTask = QueryIfSupportedAsync("guest-fsfreeze-status");
        await Task.WhenAll(osTask, usersTask, networkTask, disksTask, fsTask);
        SystemInfoText.Text = osTask.Result.Succeeded ? FormatOsInfo(osTask.Result.Output) : unavailable;
        UsersText.Text = usersTask.Result.Succeeded ? FormatUsers(usersTask.Result.Output) : unavailable;
        NetworkText.Text = networkTask.Result.Succeeded ? FormatNetwork(networkTask.Result.Output) : unavailable;
        DisksText.Text = disksTask.Result.Succeeded ? FormatDisks(disksTask.Result.Output) : unavailable;
        FsStateText.Text = fsTask.Result.Succeeded ? fsTask.Result.Output.Trim('"') : unavailable;
    }

    private Task<来宾代理结果> QueryIfSupportedAsync(string command) => Supports(command)
        ? sessions.执行来宾代理(machine, command)
        : Task.FromResult(new 来宾代理结果(false, string.Empty, "Unsupported"));

    private static string FormatOsInfo(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var name = Value(root, "pretty-name", Value(root, "name", string.Empty));
        var version = Value(root, "version", Value(root, "version-id", string.Empty));
        var kernel = Value(root, "kernel-release", string.Empty);
        var machine = Value(root, "machine", string.Empty);
        return string.Join(Environment.NewLine, new[] { name, version, kernel, machine }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string FormatUsers(string json)
    {
        using var document = JsonDocument.Parse(json);
        return string.Join(Environment.NewLine, document.RootElement.EnumerateArray().Select(item =>
        {
            var user = Value(item, "user", "?");
            var domain = Value(item, "domain", string.Empty);
            return string.IsNullOrWhiteSpace(domain) ? user : $"{domain}\\{user}";
        }));
    }

    private static string FormatNetwork(string json)
    {
        using var document = JsonDocument.Parse(json);
        var lines = new List<string>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            lines.Add(Value(item, "name", "?"));
            if (item.TryGetProperty("ip-addresses", out var addresses))
                lines.AddRange(addresses.EnumerateArray().Select(address => "  " + Value(address, "ip-address", string.Empty)).Where(value => value.Trim().Length > 0));
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatDisks(string json)
    {
        using var document = JsonDocument.Parse(json);
        return string.Join(Environment.NewLine, document.RootElement.EnumerateArray().Select(item =>
        {
            var name = Value(item, "name", "?");
            var alias = Value(item, "alias", string.Empty);
            return string.IsNullOrWhiteSpace(alias) ? name : $"{name}  {alias}";
        }));
    }

    private static string Value(JsonElement element, string property, string fallback) =>
        element.TryGetProperty(property, out var value) ? value.ToString() : fallback;

    private async void Freeze_Click(object sender, RoutedEventArgs e) => await RunMaintenanceAsync("guest-fsfreeze-freeze");
    private async void Thaw_Click(object sender, RoutedEventArgs e) => await RunMaintenanceAsync("guest-fsfreeze-thaw");
    private async void Trim_Click(object sender, RoutedEventArgs e) => await RunMaintenanceAsync("guest-fstrim");

    private async Task RunMaintenanceAsync(string command)
    {
        if (!Supports(command)) { FsStateText.Text = T("guestAgent.commandUnsupported", "当前 Guest Agent 不支持此操作。"); return; }
        var result = await sessions.执行来宾代理(machine, command);
        FsStateText.Text = result.Output;
        await RefreshOverviewAsync();
    }

    private async void RunProcess_Click(object sender, RoutedEventArgs e)
    {
        var path = ProcessPathBox.Text.Trim();
        if (path.Length == 0) { ProcessOutputBox.Text = T("guestAgent.programRequired", "请输入来宾系统中的程序路径。"); return; }
        SetProcessRunning(true);
        try
        {
            var arguments = new JsonObject
            {
                ["path"] = path,
                ["arg"] = new JsonArray(命令行.分割(ProcessArgumentsBox.Text).Select(value => (JsonNode)value).ToArray()),
                ["capture-output"] = CaptureOutputToggle.IsOn
            };
            var started = await sessions.执行来宾代理(machine, "guest-exec", arguments.ToJsonString());
            if (!started.Succeeded) { ProcessOutputBox.Text = started.Output; return; }
            using var startDocument = JsonDocument.Parse(started.Output);
            var pid = startDocument.RootElement.GetProperty("pid").GetInt64();
            ProcessOutputBox.Text = string.Format(T("guestAgent.processStarted", "进程 {0} 已启动，正在等待退出…"), pid);
            for (var attempt = 0; attempt < 600; attempt++)
            {
                await Task.Delay(250);
                var status = await sessions.执行来宾代理(machine, "guest-exec-status", $"{{\"pid\":{pid}}}");
                if (!status.Succeeded) { ProcessOutputBox.Text = status.Output; return; }
                using var statusDocument = JsonDocument.Parse(status.Output);
                if (!statusDocument.RootElement.TryGetProperty("exited", out var exited) || !exited.GetBoolean()) continue;
                ProcessOutputBox.Text = Format进程结果(statusDocument.RootElement);
                return;
            }
            ProcessOutputBox.Text = T("guestAgent.processStillRunning", "进程仍在运行；可以稍后通过高级命令查询状态。");
        }
        catch (Exception exception) { ProcessOutputBox.Text = exception.ToString(); }
        finally { SetProcessRunning(false); }
    }

    private static string Format进程结果(JsonElement root)
    {
        var lines = new List<string>();
        if (root.TryGetProperty("exitcode", out var exitCode)) lines.Add(string.Format(T("guestAgent.exitCode", "退出码 {0}"), exitCode.GetInt32()));
        if (root.TryGetProperty("signal", out var signal)) lines.Add(string.Format(T("guestAgent.signal", "信号 {0}"), signal.GetInt32()));
        if (root.TryGetProperty("out-data", out var stdout)) lines.Add(DecodeBase64(stdout.GetString()));
        if (root.TryGetProperty("err-data", out var stderr)) lines.Add(DecodeBase64(stderr.GetString()));
        return string.Join(Environment.NewLine, lines.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string DecodeBase64(string? value) => string.IsNullOrEmpty(value) ? string.Empty : Encoding.UTF8.GetString(Convert.FromBase64String(value));
    private void SetProcessRunning(bool running) { RunProcessButton.IsEnabled = !running; ProcessProgress.IsActive = running; ProcessProgress.Visibility = running ? Visibility.Visible : Visibility.Collapsed; }

    private async void BrowseHostFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, ownerHandle);
        picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync();
        if (file is not null) HostFileBox.Text = file.Path;
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(HostFileBox.Text) || string.IsNullOrWhiteSpace(GuestFileBox.Text)) { FileOutputBox.Text = T("guestAgent.filePathsRequired", "请选择宿主文件并填写来宾路径。"); return; }
        SetFileRunning(true);
        long? handle = null;
        try
        {
            handle = await OpenGuestFileAsync(GuestFileBox.Text.Trim(), "wb");
            await using var input = File.OpenRead(HostFileBox.Text);
            var buffer = new byte[FileChunkSize];
            long sent = 0;
            while (true)
            {
                var count = await input.ReadAsync(buffer);
                if (count == 0) break;
                var payload = Convert.ToBase64String(buffer, 0, count);
                var result = await sessions.执行来宾代理(machine, "guest-file-write", new JsonObject { ["handle"] = handle.Value, ["buf-b64"] = payload, ["count"] = count }.ToJsonString());
                if (!result.Succeeded) throw new IOException(result.Output);
                sent += count;
                FileOutputBox.Text = string.Format(T("guestAgent.uploadProgress", "已上传 {0} / {1}"), FormatBytes(sent), FormatBytes(input.Length));
            }
            FileOutputBox.Text = string.Format(T("guestAgent.uploadComplete", "上传完成：{0}"), GuestFileBox.Text);
        }
        catch (Exception exception) { FileOutputBox.Text = exception.Message; }
        finally { if (handle.HasValue) await CloseGuestFileAsync(handle.Value); SetFileRunning(false); }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(HostFileBox.Text) || string.IsNullOrWhiteSpace(GuestFileBox.Text)) { FileOutputBox.Text = T("guestAgent.filePathsRequired", "请选择宿主文件并填写来宾路径。"); return; }
        SetFileRunning(true);
        long? handle = null;
        try
        {
            handle = await OpenGuestFileAsync(GuestFileBox.Text.Trim(), "rb");
            await using var output = File.Create(HostFileBox.Text);
            long received = 0;
            while (true)
            {
                var result = await sessions.执行来宾代理(machine, "guest-file-read", $"{{\"handle\":{handle.Value},\"count\":{FileChunkSize}}}");
                if (!result.Succeeded) throw new IOException(result.Output);
                using var document = JsonDocument.Parse(result.Output);
                var root = document.RootElement;
                var data = root.TryGetProperty("buf-b64", out var encoded) ? Convert.FromBase64String(encoded.GetString() ?? string.Empty) : [];
                if (data.Length > 0) { await output.WriteAsync(data); received += data.Length; FileOutputBox.Text = string.Format(T("guestAgent.downloadProgress", "已下载 {0}"), FormatBytes(received)); }
                if (root.TryGetProperty("eof", out var eof) && eof.GetBoolean()) break;
            }
            FileOutputBox.Text = string.Format(T("guestAgent.downloadComplete", "下载完成：{0}"), HostFileBox.Text);
        }
        catch (Exception exception) { FileOutputBox.Text = exception.Message; }
        finally { if (handle.HasValue) await CloseGuestFileAsync(handle.Value); SetFileRunning(false); }
    }

    private async Task<long> OpenGuestFileAsync(string path, string mode)
    {
        var result = await sessions.执行来宾代理(machine, "guest-file-open", new JsonObject { ["path"] = path, ["mode"] = mode }.ToJsonString());
        if (!result.Succeeded) throw new IOException(result.Output);
        using var document = JsonDocument.Parse(result.Output);
        return document.RootElement.GetInt64();
    }

    private Task CloseGuestFileAsync(long handle) => sessions.执行来宾代理(machine, "guest-file-close", $"{{\"handle\":{handle}}}");
    private void SetFileRunning(bool running) { UploadButton.IsEnabled = DownloadButton.IsEnabled = !running; FileProgress.IsActive = running; FileProgress.Visibility = running ? Visibility.Visible : Visibility.Collapsed; }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) { if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) ApplyFilter(sender.Text); }
    private void ApplyFilter(string text) { VisibleCommands.Clear(); foreach (var command in commands.Where(item => item.Name.Contains(text, StringComparison.OrdinalIgnoreCase))) VisibleCommands.Add(command); }
    private void CommandList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (CommandList.SelectedItem is 来宾代理命令 command) CommandBox.Text = command.Name; ResetConfirmation(); }
    private void ArgumentsBox_TextChanged(object sender, TextChangedEventArgs e) => ResetConfirmation();
    private async void ExecuteButton_Click(object sender, RoutedEventArgs e) => await ExecuteAdvancedAsync(CommandBox.Text.Trim(), ArgumentsBox.Text.Trim());

    private async Task ExecuteAdvancedAsync(string command, string arguments)
    {
        if (command.Length == 0) return;
        if (ConfirmedCommands.Contains(command) && !confirmationPending) { confirmationPending = true; ExecuteButtonText.Text = T("common.confirmExecute", "确认执行"); OutputBox.Text = T("guestAgent.dangerousConfirm", "该命令可能改变来宾系统状态。请检查参数后再次执行。"); return; }
        ResetConfirmation();
        SetAdvancedRunning(true);
        try { OutputBox.Text = (await sessions.执行来宾代理(machine, command, arguments is "" or "{}" ? string.Empty : arguments)).Output; }
        catch (Exception exception) { OutputBox.Text = exception.ToString(); }
        finally { SetAdvancedRunning(false); }
    }

    private void SetAdvancedRunning(bool running) { ExecuteButton.IsEnabled = !running; Progress.IsActive = running; Progress.Visibility = running ? Visibility.Visible : Visibility.Collapsed; }
    private void ResetConfirmation() { confirmationPending = false; ExecuteButtonText.Text = T("common.execute", "执行"); }
}
