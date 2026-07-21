using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QemuWG.数据;
using QemuWG.服务;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace QemuWG.界面;

public sealed partial class 虚拟机编辑 : ContentDialog
{
    private static string T(string key, string fallback) => 语言服务.当前.获取(key, fallback);

    private readonly nint ownerHandle;
    private readonly QEMU安装 install;
    private readonly QEMU服务 qemuSvc;
    private readonly 虚拟机配置? source;
    private readonly HashSet<ComboBox> modifiedCapabilityFields = [];
    private int capsVersion;
    private bool updatingCapabilityControls;

    public ObservableCollection<QEMU选项> ConfiguredQemuOpts { get; } = [];
    public ObservableCollection<QEMU设备> ConfiguredDevices { get; } = [];
    public ObservableCollection<QEMU选项> SelectedDeviceProperties { get; } = [];
    private IReadOnlyList<QEMU设备属性> availableDeviceProperties = [];

    public 虚拟机编辑(
        nint ownerHandle,
        QEMU安装 install,
        QEMU服务 qemuSvc,
        string defaultLocation,
        虚拟机配置? source = null)
    {
        InitializeComponent();
        对话框布局.启用自适应尺寸(this);
        this.ownerHandle = ownerHandle;
        this.install = install;
        this.qemuSvc = qemuSvc;
        this.source = source;

        Title = source is null ? T("vmEditor.newTitle", "新建虚拟机") : T("vmEditor.editTitle", "编辑虚拟机");
        PrimaryButtonText = source is null ? T("common.create", "创建") : T("common.save", "保存");
        ArchCombo.ItemsSource = install.Archs;

        var vm = source?.Copy() ?? new 虚拟机配置();
        NameBox.Text = vm.Name;
        IsoBox.Text = vm.IsoPath;
        LocationBox.Text = source?.DirPath ?? defaultLocation;
        MemoryBox.Value = vm.MemoryMb;
        CpuCountBox.Value = vm.CpuCount;
        DiskBox.Value = vm.DiskGb;
        ExtraArgumentsBox.Text = vm.ExtraArgs;
        GuestAgentToggle.IsOn = vm.EnableGuestAgent;
        foreach (var option in vm.QemuOpts)
            ConfiguredQemuOpts.Add(new QEMU选项 { Name = option.Name, Value = option.Value });
        foreach (var device in vm.Devices)
            ConfiguredDevices.Add(new QEMU设备
            {
                Model = device.Model,
                Properties = new Dictionary<string, string>(device.Properties, StringComparer.OrdinalIgnoreCase)
            });

        ArchCombo.SelectedItem = install.Archs.FirstOrDefault(item => string.Equals(item.Id, vm.Arch, StringComparison.OrdinalIgnoreCase));
        if (ArchCombo.SelectedItem is null) ArchCombo.Text = vm.Arch;
        SelectTaggedItem(FirmwareCombo, vm.Firmware);
        SelectStringItem(BootOrderCombo, vm.BootOrder);
        SelectStringItem(RtcCombo, vm.RtcBase);

        if (source is not null)
        {
            BrowseLocationButton.IsEnabled = false;
            DiskBox.IsEnabled = false;
        }

        Loaded += async (_, _) =>
        {
            await LoadCapsAsync(vm);
            EditorScrollViewer.ChangeView(null, 0, null, true);
        };
    }

    public string ParentDir => source is null ? LocationBox.Text.Trim() : Path.GetDirectoryName(source.DirPath) ?? LocationBox.Text.Trim();

    public 虚拟机配置 BuildMachine()
    {
        var vm = source?.Copy() ?? new 虚拟机配置();
        vm.Name = NameBox.Text.Trim();
        vm.IsoPath = IsoBox.Text.Trim();
        vm.Arch = ReadArchitecture();
        vm.Firmware = SelectedTag(FirmwareCombo, "uefi");
        vm.MachineType = ReadCapabilityValue(MachineCombo, source?.MachineType, string.Empty);
        vm.Accelerator = ReadCapabilityValue(AcceleratorCombo, source?.Accelerator, "tcg");
        vm.CpuModel = ReadCapabilityValue(CpuModelCombo, source?.CpuModel, "default");
        vm.MemoryMb = (int)MemoryBox.Value;
        vm.CpuCount = (int)CpuCountBox.Value;
        vm.DiskGb = (int)DiskBox.Value;
        vm.DisplayBackend = ReadCapabilityValue(DisplayCombo, source?.DisplayBackend, "dbus");
        vm.VideoDevice = ReadCapabilityValue(VideoCombo, source?.VideoDevice, "auto");
        vm.AudioBackend = ReadCapabilityValue(AudioCombo, source?.AudioBackend, "none");
        vm.AudioDevice = ReadCapabilityValue(AudioDeviceCombo, source?.AudioDevice, "auto");
        vm.KeyboardDevice = ReadCapabilityValue(KeyboardDeviceCombo, source?.KeyboardDevice, "auto");
        vm.MouseDevice = ReadCapabilityValue(MouseDeviceCombo, source?.MouseDevice, source is null ? "usb-tablet" : "auto");
        vm.NetworkMode = ReadCapabilityValue(NetworkModeCombo, source?.NetworkMode, "user");
        vm.NetworkModel = ReadCapabilityValue(NetworkModelCombo, source?.NetworkModel, "auto");
        vm.BootOrder = NormalizeDefault(BootOrderCombo.SelectedItem?.ToString(), "dc");
        vm.RtcBase = NormalizeDefault(RtcCombo.SelectedItem?.ToString(), "localtime");
        vm.ExtraArgs = ExtraArgumentsBox.Text.Trim();
        vm.EnableGuestAgent = GuestAgentToggle.IsOn;
        vm.QemuOpts = ConfiguredQemuOpts
            .Select(option => new QEMU选项 { Name = option.Name, Value = option.Value })
            .ToList();
        vm.Devices = ConfiguredDevices.Select(device => new QEMU设备
        {
            Model = device.Model,
            Properties = new Dictionary<string, string>(device.Properties, StringComparer.OrdinalIgnoreCase)
        }).ToList();
        return vm;
    }

    private async void BrowseIso_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, ownerHandle);
        picker.FileTypeFilter.Add(".iso");
        picker.FileTypeFilter.Add(".img");
        picker.FileTypeFilter.Add(".qcow2");
        var file = await picker.PickSingleFileAsync();
        if (file is not null) IsoBox.Text = file.Path;
    }

    private async void BrowseLocation_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, ownerHandle);
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) LocationBox.Text = folder.Path;
    }

    private async void ArchitectureCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (!updatingCapabilityControls) modifiedCapabilityFields.Add(ArchCombo);
        await LoadCapsAsync();
    }

    private async Task LoadCapsAsync(虚拟机配置? preferred = null)
    {
        if (ArchCombo.SelectedItem is not QEMU架构 arch) return;
        var version = ++capsVersion;
        CapsInfoText.Text = T("vmEditor.capabilityLoading", "正在读取该架构的 QEMU 能力…");

        try
        {
            var caps = await qemuSvc.获取能力(arch);
            if (version != capsVersion) return;

            ApplyCapabilityLists(caps, preferred);
            CapsInfoText.Text = string.Format(
                T("vmEditor.capabilitySummary", "{0} · {1} 种机器 · {2} 种 CPU · {3} 种声卡 · {4} 种设备"),
                arch.DisplayName,
                caps.Machines.Count,
                caps.CpuModels.Count,
                caps.AudioDevices.Count,
                caps.AllDevices.Count);
        }
        catch (Exception exception)
        {
            if (version != capsVersion) return;
            ApplyCapabilityLists(new QEMU能力(), preferred);
            CapsInfoText.Text = string.Format(T("vmEditor.capabilityFailed", "能力读取失败：{0}"), exception.Message);
        }
    }

    private void ApplyCapabilityLists(QEMU能力 caps, 虚拟机配置? preferred)
    {
        var machine = preferred?.MachineType ?? CurrentComboValue(MachineCombo);
        var cpu = preferred?.CpuModel ?? CurrentComboValue(CpuModelCombo);
        var accelerator = preferred?.Accelerator ?? CurrentComboValue(AcceleratorCombo);
        var display = preferred?.DisplayBackend ?? CurrentComboValue(DisplayCombo);
        var video = preferred?.VideoDevice ?? CurrentComboValue(VideoCombo);
        var networkBackend = preferred?.NetworkMode ?? CurrentComboValue(NetworkModeCombo);
        var networkDevice = preferred?.NetworkModel ?? CurrentComboValue(NetworkModelCombo);
        var audioBackend = preferred?.AudioBackend ?? CurrentComboValue(AudioCombo);
        var audioDevice = preferred?.AudioDevice ?? CurrentComboValue(AudioDeviceCombo);
        var keyboardDevice = preferred?.KeyboardDevice ?? CurrentComboValue(KeyboardDeviceCombo);
        var mouseDevice = preferred?.MouseDevice ?? CurrentComboValue(MouseDeviceCombo);
        if (source is null && (string.IsNullOrWhiteSpace(mouseDevice) || string.Equals(mouseDevice, "auto", StringComparison.OrdinalIgnoreCase)))
            mouseDevice = caps.PointerDevices.FirstOrDefault(device => string.Equals(device, "usb-tablet", StringComparison.OrdinalIgnoreCase))
                          ?? caps.PointerDevices.FirstOrDefault(device => device.Contains("tablet", StringComparison.OrdinalIgnoreCase))
                          ?? "auto";

        updatingCapabilityControls = true;
        try
        {
            SetComboItems(MachineCombo, caps.Machines, machine);
            SetComboItems(CpuModelCombo, caps.CpuModels, cpu);
            SetComboItems(AcceleratorCombo, caps.Accelerators, accelerator);
            SetComboItems(DisplayCombo, caps.DisplayBackends, display);
            SetComboItems(VideoCombo, caps.VideoDevices, video);
            SetComboItems(NetworkModeCombo, caps.NetworkBackends, networkBackend);
            SetComboItems(NetworkModelCombo, caps.NetworkDevices, networkDevice);
            SetComboItems(AudioCombo, caps.AudioBackends, audioBackend);
            SetComboItems(AudioDeviceCombo, caps.AudioDevices, audioDevice);
            SetComboItems(KeyboardDeviceCombo, 添加自动选项(caps.KeyboardDevices), keyboardDevice);
            SetComboItems(MouseDeviceCombo, 添加自动选项(caps.PointerDevices), mouseDevice);
            QemuOptCombo.ItemsSource = caps.CmdOptions;
            var inputModels = new HashSet<string>(caps.InputDevices, StringComparer.OrdinalIgnoreCase);
            DeviceModelCombo.ItemsSource = caps.AllDevices.Where(device => !inputModels.Contains(device)).ToList();
        }
        finally
        {
            updatingCapabilityControls = false;
        }
    }

    private void QemuOptionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QemuOptCombo.SelectedItem is not QEMU命令选项 option) return;
        QemuOptHintText.Text = string.IsNullOrWhiteSpace(option.Syntax) ? option.Name : $"{option.Name} {option.Syntax}";
    }

    private void AddQemuOption_Click(object sender, RoutedEventArgs e)
    {
        var name = QemuOptCombo.SelectedItem is QEMU命令选项 definition
            ? definition.Name
            : QemuOptCombo.Text.Trim();
        if (name.Length == 0) return;
        ConfiguredQemuOpts.Add(new QEMU选项 { Name = name, Value = QemuOptValueBox.Text.Trim() });
        QemuOptValueBox.Text = string.Empty;
    }

    private void RemoveQemuOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: QEMU选项 option }) ConfiguredQemuOpts.Remove(option);
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var error = ValidateInput();
        if (error is null) return;
        args.Cancel = true;
        ValidationInfo.Message = error;
        ValidationInfo.IsOpen = true;
    }

    private string? ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text)) return T("vmEditor.validation.name", "请输入虚拟机名称。");
        if (ArchCombo.SelectedItem is null && string.IsNullOrWhiteSpace(ArchCombo.Text)) return T("vmEditor.validation.architecture", "请选择系统架构。");
        if (string.IsNullOrWhiteSpace(LocationBox.Text)) return T("vmEditor.validation.location", "请选择保存位置。");
        if (!double.IsFinite(MemoryBox.Value) || MemoryBox.Value < 256) return T("vmEditor.validation.memory", "内存至少为 256 MB。");
        if (!double.IsFinite(CpuCountBox.Value) || CpuCountBox.Value < 1) return T("vmEditor.validation.cpu", "处理器数量至少为 1。");
        if (!double.IsFinite(DiskBox.Value) || DiskBox.Value < 1) return T("vmEditor.validation.disk", "磁盘至少为 1 GB。");
        if (!string.IsNullOrWhiteSpace(IsoBox.Text) && !File.Exists(IsoBox.Text)) return T("vmEditor.validation.media", "安装镜像不存在。");
        return null;
    }

    private static void SetComboItems(ComboBox comboBox, IReadOnlyList<string> items, string? selected)
    {
        comboBox.ItemsSource = items;
        comboBox.SelectedItem = items.FirstOrDefault(item => string.Equals(item, selected, StringComparison.OrdinalIgnoreCase));
        if (comboBox.SelectedItem is null) comboBox.Text = selected ?? string.Empty;
    }

    private static IReadOnlyList<string> 添加自动选项(IReadOnlyList<string> items) => ["auto", "none", .. items];

    private void CapabilityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!updatingCapabilityControls && IsLoaded && sender is ComboBox comboBox)
            modifiedCapabilityFields.Add(comboBox);
    }

    private void CapabilityCombo_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!updatingCapabilityControls && IsLoaded && sender is ComboBox comboBox)
            modifiedCapabilityFields.Add(comboBox);
    }

    private string ReadArchitecture()
    {
        if (source is not null && !modifiedCapabilityFields.Contains(ArchCombo)) return source.Arch;
        var value = (ArchCombo.SelectedItem as QEMU架构)?.Id ?? ArchCombo.Text;
        return string.IsNullOrWhiteSpace(value) ? "x86_64" : value.Trim();
    }

    private string ReadCapabilityValue(ComboBox comboBox, string? originalValue, string fallback)
    {
        if (source is not null && !modifiedCapabilityFields.Contains(comboBox)) return originalValue ?? string.Empty;
        var value = CurrentComboValue(comboBox);
        if (modifiedCapabilityFields.Contains(comboBox)) return value.Trim();
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string CurrentComboValue(ComboBox comboBox) =>
        comboBox.SelectedItem?.ToString() ?? comboBox.Text ?? string.Empty;

    private static void SelectTaggedItem(ComboBox comboBox, string value)
    {
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase));
        if (comboBox.SelectedItem is null) comboBox.SelectedIndex = 0;
    }

    private static string SelectedTag(ComboBox comboBox, string fallback) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private static void SelectStringItem(ComboBox comboBox, string value)
    {
        comboBox.SelectedItem = comboBox.Items.Cast<object>().FirstOrDefault(item =>
            string.Equals((item as ComboBoxItem)?.Content?.ToString() ?? item.ToString(), value, StringComparison.OrdinalIgnoreCase));
        if (comboBox.SelectedItem is null) comboBox.SelectedIndex = 0;
    }

    private static string NormalizeDefault(string? value, string fallback = "") =>
        string.IsNullOrWhiteSpace(value) || value == "default" ? fallback : value.Trim();
}
