using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QemuWG.数据;
using QemuWG.服务;

namespace QemuWG.界面;

public sealed partial class 快照管理器 : ContentDialog
{
    private static string T(string key, string fallback) => 语言服务.当前.获取(key, fallback);

    private readonly 快照服务 snapshotService;
    private readonly 仿真配置 machine;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly ObservableCollection<快照树项> treeItems = [];
    private 快照信息? selectedSnapshot;
    private 确认操作 pendingOperation;
    private bool busy;
    private bool compactLayout;

    public 快照管理器(nint ownerHandle, QEMU安装 install, QEMU会话 sessions, 仿真配置 machine)
    {
        InitializeComponent();
        对话框布局.启用自适应尺寸(this);
        this.machine = machine;
        snapshotService = new 快照服务(install, sessions);
        Title = T("snapshot.title", "快照管理器");
        MachineNameText.Text = machine.Name;
        CreateHintText.Text = machine.IsRunning
            ? T("snapshot.createHint", "快照会保存磁盘、内存和设备状态；创建时间取决于当前内存用量。")
            : T("snapshot.createHintStopped", "仿真已关机，将创建磁盘与 UEFI 状态快照，不包含内存。");
        SnapshotList.ItemsSource = treeItems;
        Loaded += async (_, _) => await RefreshAsync();
        Closing += SnapshotManager_Closing;
        Closed += (_, _) => lifetimeCancellation.Cancel();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync(string? preferredTag = null)
    {
        if (busy) return;
        preferredTag ??= selectedSnapshot?.Tag;
        await RunBusyAsync(T("snapshot.refreshing", "正在读取快照…"), async cancellationToken =>
        {
            var result = await snapshotService.查询(machine, cancellationToken);
            if (!result.Succeeded)
            {
                treeItems.Clear();
                selectedSnapshot = null;
                ShowResult(result.Message, true);
                SummaryText.Text = T("snapshot.unavailable", "无法读取快照");
                UpdateDetails(null);
                return;
            }

            BuildTree(result.Snapshots, result.CurrentParentTag);
            SummaryText.Text = string.Format(
                T("snapshot.summary", "{0} 个快照 · {1}"),
                result.Snapshots.Count,
                machine.IsRunning ? T("snapshot.running", "仿真运行中") : T("snapshot.stopped", "仿真已关机"));

            var preferredItem = treeItems.FirstOrDefault(item => item.快照 is not null
                && string.Equals(item.快照.Tag, preferredTag, StringComparison.Ordinal));
            SnapshotList.SelectedItem = preferredItem;
            if (preferredItem is null) UpdateDetails(null);

        });
    }

    private void BuildTree(IReadOnlyList<快照信息> snapshots, string currentParentTag)
    {
        treeItems.Clear();
        var byTag = snapshots
            .Where(snapshot => !string.IsNullOrWhiteSpace(snapshot.Tag))
            .GroupBy(snapshot => snapshot.Tag, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var children = new Dictionary<string, List<快照信息>>(StringComparer.Ordinal);
        foreach (var snapshot in byTag.Values)
        {
            var parent = byTag.ContainsKey(snapshot.ParentTag) ? snapshot.ParentTag : string.Empty;
            if (!children.TryGetValue(parent, out var list)) children[parent] = list = [];
            list.Add(snapshot);
        }
        foreach (var list in children.Values) list.Sort((left, right) => left.CreatedAt.CompareTo(right.CreatedAt));

        var visited = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(currentParentTag)) treeItems.Add(CreateCurrentStateItem(0));
        AddChildren(string.Empty, 0);

        // 损坏或循环的元数据仍然要可见，不能悄悄丢掉真实 QEMU 快照。
        foreach (var snapshot in byTag.Values.Where(snapshot => !visited.Contains(snapshot.Tag)).OrderBy(snapshot => snapshot.CreatedAt))
            AddSnapshot(snapshot, 0);
        return;

        void AddChildren(string parentTag, int depth)
        {
            if (!children.TryGetValue(parentTag, out var list)) return;
            foreach (var child in list) AddSnapshot(child, depth);
        }

        void AddSnapshot(快照信息 snapshot, int depth)
        {
            if (!visited.Add(snapshot.Tag)) return;
            treeItems.Add(new 快照树项(snapshot, depth));
            if (string.Equals(snapshot.Tag, currentParentTag, StringComparison.Ordinal))
                treeItems.Add(CreateCurrentStateItem(depth + 1));
            AddChildren(snapshot.Tag, depth + 1);
        }
    }

    private static 快照树项 CreateCurrentStateItem(int depth) => new(
        T("snapshot.currentState", "当前状态"),
        T("snapshot.currentStateDetail", "尚未保存的当前运行状态"),
        depth);

    private void SnapshotList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CancelConfirmation();
        selectedSnapshot = (SnapshotList.SelectedItem as 快照树项)?.快照;
        UpdateDetails(selectedSnapshot);
    }

    private void UpdateDetails(快照信息? snapshot)
    {
        var hasSnapshot = snapshot is not null;
        NoSelectionPanel.Visibility = hasSnapshot ? Visibility.Collapsed : Visibility.Visible;
        SnapshotDetailPanel.Visibility = hasSnapshot ? Visibility.Visible : Visibility.Collapsed;
        if (snapshot is null) return;

        DetailNameText.Text = snapshot.Name;
        DetailStatusText.Text = snapshot.可用
            ? (snapshot.含内存 ? T("snapshot.completeState", "包含磁盘、内存和设备状态") : T("snapshot.diskState", "磁盘状态"))
            : T("snapshot.broken", "快照不可用");
        DetailDescriptionText.Text = string.IsNullOrWhiteSpace(snapshot.Description)
            ? T("snapshot.noDescription", "没有说明")
            : snapshot.Description;
        DetailCreatedAtText.Text = snapshot.CreatedAt == default
            ? T("snapshot.unknown", "未知")
            : snapshot.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        DetailMemoryText.Text = snapshot.含内存
            ? string.Format(T("snapshot.memorySize", "已保存 · {0}"), FormatBytes(snapshot.VmStateSize))
            : T("snapshot.noMemory", "未保存内存状态");
        DetailClockText.Text = string.IsNullOrWhiteSpace(snapshot.VmClock) ? T("snapshot.unknown", "未知") : snapshot.VmClock;
        DetailTagText.Text = snapshot.Tag;
        SnapshotProblemBar.IsOpen = !snapshot.可用 || !string.IsNullOrWhiteSpace(snapshot.问题);
        SnapshotProblemBar.Title = T("snapshot.problem", "快照存在问题");
        SnapshotProblemBar.Message = snapshot.问题;
        RestoreButton.IsEnabled = snapshot.可用;
        DeleteButton.IsEnabled = true;
    }

    private void SnapshotNameBox_TextChanged(object sender, TextChangedEventArgs e) =>
        CreateButton.IsEnabled = !busy && !string.IsNullOrWhiteSpace(SnapshotNameBox.Text);

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var name = SnapshotNameBox.Text.Trim();
        if (name.Length == 0) return;
        CancelConfirmation();
        await RunBusyAsync(T("snapshot.creating", "正在创建快照…"), async cancellationToken =>
        {
            var result = await snapshotService.创建(machine, name, SnapshotDescriptionBox.Text.Trim(), cancellationToken);
            ShowResult(FormatResult(result), !result.Succeeded);
            if (!result.Succeeded) return;
            SnapshotNameBox.Text = string.Empty;
            SnapshotDescriptionBox.Text = string.Empty;
            await RefreshAfterOperationAsync(cancellationToken);
        });
    }

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedSnapshot is null) return;
        BeginConfirmation(
            确认操作.恢复,
            T("snapshot.restoreConfirmTitle", "恢复此快照？"),
            T("snapshot.restoreConfirmMessage", "当前未保存的状态将被替换。运行中的仿真会回到创建快照时的状态。"),
            T("snapshot.confirmRestore", "确认恢复"));
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedSnapshot is null) return;
        BeginConfirmation(
            确认操作.删除,
            T("snapshot.deleteConfirmTitle", "删除此快照？"),
            T("snapshot.deleteConfirmMessage", "该操作不可撤销；其子快照会保留并接到上一级分支。"),
            T("snapshot.confirmDelete", "确认删除"));
    }

    private void BeginConfirmation(确认操作 operation, string title, string message, string buttonText)
    {
        pendingOperation = operation;
        ConfirmationTitleText.Text = title;
        ConfirmationMessageText.Text = message;
        ConfirmOperationButton.Content = buttonText;
        ConfirmationBar.Visibility = Visibility.Visible;
    }

    private void CancelConfirmation_Click(object sender, RoutedEventArgs e) => CancelConfirmation();

    private void CancelConfirmation()
    {
        pendingOperation = 确认操作.无;
        ConfirmationBar.Visibility = Visibility.Collapsed;
    }

    private async void ConfirmOperationButton_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = selectedSnapshot;
        var operation = pendingOperation;
        if (snapshot is null || operation == 确认操作.无) return;
        CancelConfirmation();

        await RunBusyAsync(
            operation == 确认操作.恢复 ? T("snapshot.restoring", "正在恢复快照…") : T("snapshot.deleting", "正在删除快照…"),
            async cancellationToken =>
            {
                var result = operation == 确认操作.恢复
                    ? await snapshotService.恢复(machine, snapshot, cancellationToken)
                    : await snapshotService.删除(machine, snapshot, cancellationToken);
                ShowResult(FormatResult(result), !result.Succeeded);
                if (!result.Succeeded) return;
                await RefreshAfterOperationAsync(cancellationToken);
            });
    }

    private async Task RefreshAfterOperationAsync(CancellationToken cancellationToken)
    {
        var result = await snapshotService.查询(machine, cancellationToken);
        if (!result.Succeeded)
        {
            ShowResult(result.Message, true);
            return;
        }
        BuildTree(result.Snapshots, result.CurrentParentTag);
        SummaryText.Text = string.Format(
            T("snapshot.summary", "{0} 个快照 · {1}"),
            result.Snapshots.Count,
            machine.IsRunning ? T("snapshot.running", "仿真运行中") : T("snapshot.stopped", "仿真已关机"));
        SnapshotList.SelectedItem = null;
        selectedSnapshot = null;
        UpdateDetails(null);
    }

    private async Task RunBusyAsync(string message, Func<CancellationToken, Task> operation)
    {
        if (busy) return;
        busy = true;
        BusyText.Text = message;
        BusyOverlay.Visibility = Visibility.Visible;
        RefreshButton.IsEnabled = false;
        CreateButton.IsEnabled = false;
        try
        {
            await operation(lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            应用日志.写("Snapshot manager operation failed: " + exception);
            ShowResult(exception.Message, true);
        }
        finally
        {
            busy = false;
            BusyOverlay.Visibility = Visibility.Collapsed;
            RefreshButton.IsEnabled = true;
            CreateButton.IsEnabled = !string.IsNullOrWhiteSpace(SnapshotNameBox.Text);
        }
    }

    private void ShowResult(string message, bool error)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        ResultBar.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Success;
        ResultBar.Title = error ? T("dialog.operationFailed", "操作失败") : T("snapshot.operationComplete", "操作完成");
        ResultBar.Message = message;
        ResultBar.IsOpen = true;
    }

    private static string FormatResult(操作结果 result) => string.IsNullOrWhiteSpace(result.Detail)
        ? result.Message
        : result.Message + Environment.NewLine + result.Detail;

    private void Workspace_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
        var compact = e.NewSize.Width < 720;
        if (compact == compactLayout) return;
        compactLayout = compact;
        if (compact)
        {
            TreeColumn.Width = new GridLength(1, GridUnitType.Star);
            DetailColumn.Width = new GridLength(0);
            TreeRow.Height = new GridLength(210);
            DetailRow.Height = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(TreeCard, 0);
            Grid.SetRow(TreeCard, 0);
            Grid.SetColumn(DetailScrollViewer, 0);
            Grid.SetRow(DetailScrollViewer, 1);
            Grid.SetRowSpan(BusyOverlay, 2);
            return;
        }

        TreeColumn.Width = new GridLength(330);
        DetailColumn.Width = new GridLength(1, GridUnitType.Star);
        TreeRow.Height = new GridLength(1, GridUnitType.Star);
        DetailRow.Height = new GridLength(0);
        Grid.SetColumn(TreeCard, 0);
        Grid.SetRow(TreeCard, 0);
        Grid.SetColumn(DetailScrollViewer, 1);
        Grid.SetRow(DetailScrollViewer, 0);
        Grid.SetRowSpan(BusyOverlay, 1);
    }

    private void SnapshotManager_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (!busy) return;
        args.Cancel = true;
        ShowResult(T("snapshot.waitForOperation", "请等待当前快照操作完成。"), true);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, (double)bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private enum 确认操作
    {
        无,
        恢复,
        删除
    }
}

public sealed class 快照树项
{
    private static readonly SolidColorBrush 快照颜色 = new(ColorHelper.FromArgb(255, 91, 141, 239));
    private static readonly SolidColorBrush 当前状态颜色 = new(ColorHelper.FromArgb(255, 73, 171, 105));

    public 快照树项(快照信息 snapshot, int depth)
    {
        快照 = snapshot;
        标题 = snapshot.Name;
        副标题 = snapshot.CreatedAt == default ? snapshot.Tag : snapshot.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        缩进 = new Thickness(Math.Min(depth, 12) * 18, 0, 0, 0);
        图标 = "\uE7B8";
        图标颜色 = 快照颜色;
        当前标记可见 = Visibility.Collapsed;
    }

    public 快照树项(string title, string subtitle, int depth)
    {
        标题 = title;
        副标题 = subtitle;
        缩进 = new Thickness(Math.Min(depth, 12) * 18, 0, 0, 0);
        图标 = "\uE768";
        图标颜色 = 当前状态颜色;
        当前标记可见 = Visibility.Visible;
    }

    public 快照信息? 快照 { get; }
    public string 标题 { get; }
    public string 副标题 { get; }
    public Thickness 缩进 { get; }
    public string 图标 { get; }
    public SolidColorBrush 图标颜色 { get; }
    public Visibility 当前标记可见 { get; }
}
