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

    private void BuildForm(DiskCmdDef command)
    {
        inputs.Clear();
        fieldContainers.Clear();
        FormGrid.Children.Clear();
        FormGrid.RowDefinitions.Clear();
        awaitingConfirmation = false;
        SafetyInfo.IsOpen = false;
        ExecuteButtonText.Text = T("common.execute", "执行");
        OutputBox.Text = string.Empty;
        ExitCodeText.Text = command.Description;

        var row = 0;
        var column = 0;
        foreach (var field in command.Fields)
        {
            if (field.Wide && column != 0)
            {
                row++;
                column = 0;
            }
            while (FormGrid.RowDefinitions.Count <= row)
                FormGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var container = CreateField(field);
            Grid.SetRow(container, row);
            Grid.SetColumn(container, column);
            if (field.Wide)
            {
                Grid.SetColumnSpan(container, 2);
                row++;
                column = 0;
            }
            else if (column == 0)
            {
                column = 1;
            }
            else
            {
                row++;
                column = 0;
            }
            FormGrid.Children.Add(container);
            fieldContainers[field.Id] = container;
        }

        UpdateDependencies();
        UpdatePreview();
        ExecuteButton.IsEnabled = !(machine.IsRunning && command.CanWrite);
        if (!ExecuteButton.IsEnabled)
        {
            SafetyInfo.Message = T("disk.runningBlocked", "虚拟机运行时禁止执行会修改磁盘的命令。");
            SafetyInfo.IsOpen = true;
        }
    }

    private FrameworkElement CreateField(DiskFieldDef field)
    {
        if (field.Mode == DiskFieldMode.Flag)
        {
            var toggle = new ToggleSwitch
            {
                Header = field.Label,
                OnContent = T("common.on", "开"),
                OffContent = T("common.off", "关"),
                IsOn = string.Equals(field.DefaultValue, "true", StringComparison.OrdinalIgnoreCase)
            };
            toggle.Toggled += InputChanged;
            inputs[field.Id] = toggle;
            return toggle;
        }

        var panel = new StackPanel { Spacing = 3 };
        panel.Children.Add(new TextBlock
        {
            Text = field.Label,
            FontSize = 11
        });

        if (field.Choices.Count > 0)
        {
            var combo = new ComboBox
            {
                ItemsSource = field.Choices,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxDropDownHeight = 240
            };
            combo.SelectedItem = field.Choices.FirstOrDefault(value => value == field.DefaultValue) ?? field.Choices.FirstOrDefault();
            combo.SelectionChanged += InputChanged;
            panel.Children.Add(combo);
            inputs[field.Id] = combo;
            return panel;
        }

        var textBox = new TextBox { Text = DefaultValue(field) };
        textBox.TextChanged += InputChanged;
        inputs[field.Id] = textBox;
        if (field.PathKind == DiskPathKind.None)
        {
            panel.Children.Add(textBox);
            return panel;
        }

        var pathGrid = new Grid { ColumnSpacing = 6 };
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pathGrid.Children.Add(textBox);
        var browse = new Button { Content = new SymbolIcon(Symbol.Folder), CornerRadius = new CornerRadius(6) };
        browse.Click += async (_, _) => await BrowsePathAsync(field, textBox);
        Grid.SetColumn(browse, 1);
        pathGrid.Children.Add(browse);
        panel.Children.Add(pathGrid);
        return panel;
    }

    private string DefaultValue(DiskFieldDef field)
    {
        if (!string.IsNullOrEmpty(field.DefaultValue)) return field.DefaultValue;
        return field.Id switch
        {
            "file" or "file1" or "sources" or "input" => machine.DiskPath,
            _ => string.Empty
        };
    }

    private async Task BrowsePathAsync(DiskFieldDef field, TextBox textBox)
    {
        if (field.PathKind == DiskPathKind.Save)
        {
            var picker = new FileSavePicker { SuggestedFileName = "disk" };
            InitializeWithWindow.Initialize(picker, ownerHandle);
            picker.FileTypeChoices.Add(T("disk.imageFileType", "磁盘镜像"), [".qcow2", ".img", ".raw", ".vmdk", ".vhdx"]);
            var file = await picker.PickSaveFileAsync();
            if (file is not null) textBox.Text = file.Path;
            return;
        }

        var openPicker = new FileOpenPicker();
        InitializeWithWindow.Initialize(openPicker, ownerHandle);
        openPicker.FileTypeFilter.Add("*");
        if (field.Mode == DiskFieldMode.MultiPositional)
        {
            var files = await openPicker.PickMultipleFilesAsync();
            if (files.Count > 0) textBox.Text = string.Join(';', files.Select(file => file.Path));
        }
        else
        {
            var file = await openPicker.PickSingleFileAsync();
            if (file is not null) textBox.Text = file.Path;
        }
    }

    private void InputChanged(object sender, object e)
    {
        awaitingConfirmation = false;
        ExecuteButtonText.Text = T("common.execute", "执行");
        if (!(machine.IsRunning && selectedCmd?.CanWrite == true)) SafetyInfo.IsOpen = false;
        UpdateDependencies();
        UpdatePreview();
    }

    private void UpdateDependencies()
    {
        if (selectedCmd is null) return;
        var values = ReadValues();
        foreach (var field in selectedCmd.Fields)
        {
            if (string.IsNullOrEmpty(field.DependsOnId) || !fieldContainers.TryGetValue(field.Id, out var container)) continue;
            values.TryGetValue(field.DependsOnId, out var current);
            var matches = string.Equals(current, field.DependsOnValue, StringComparison.OrdinalIgnoreCase);
            container.Visibility = (field.InvertDependency ? !matches : matches) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdatePreview()
    {
        if (selectedCmd is null) return;
        try
        {
            CommandPreviewBox.Text = qemuImgSvc.BuildArgs(selectedCmd, ReadValues()).Preview;
        }
        catch (InvalidOperationException)
        {
            CommandPreviewBox.Text = "qemu-img " + selectedCmd.Name;
        }
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

    private Dictionary<string, string> ReadValues()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, control) in inputs)
        {
            values[id] = control switch
            {
                TextBox textBox => textBox.Text,
                ComboBox comboBox => comboBox.SelectedItem?.ToString() ?? string.Empty,
                ToggleSwitch toggle => toggle.IsOn ? "true" : "false",
                _ => string.Empty
            };
        }
        return values;
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


