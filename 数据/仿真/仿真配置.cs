using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using QemuWG.服务;

namespace QemuWG.数据;

public sealed class 仿真配置 : INotifyPropertyChanged
{
    private bool isRunning;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Windows";
    public string DirPath { get; set; } = string.Empty;
    public string CfgPath { get; set; } = string.Empty;
    public string DiskPath { get; set; } = string.Empty;
    public string IsoPath { get; set; } = string.Empty;
    public string 来宾系统 { get; set; } = 仿真系统标识.自动;
    public string Arch { get; set; } = "x86_64";
    public string Firmware { get; set; } = "uefi";
    public string MachineType { get; set; } = string.Empty;
    public string Accelerator { get; set; } = "auto";
    public string CpuModel { get; set; } = "default";
    public string DisplayBackend { get; set; } = "dbus";
    public string VideoDevice { get; set; } = "auto";
    public int DisplayWidth { get; set; }
    public int DisplayHeight { get; set; }
    public string AudioBackend { get; set; } = "none";
    public string AudioDevice { get; set; } = "auto";
    public string KeyboardDevice { get; set; } = "auto";
    public string MouseDevice { get; set; } = "auto";
    public string NetworkMode { get; set; } = "user";
    public string NetworkModel { get; set; } = "auto";
    public string NetworkMac { get; set; } = string.Empty;
    public string HostForwarding { get; set; } = string.Empty;
    public string DiskInterface { get; set; } = "virtio";
    public string DiskCache { get; set; } = "default";
    public string DiskAio { get; set; } = "default";
    public string DiskDiscard { get; set; } = "default";
    public string DiskDetectZeroes { get; set; } = "default";
    public string BootOrder { get; set; } = "dc";
    public string BootOnce { get; set; } = string.Empty;
    public string RtcBase { get; set; } = "localtime";
    public string KeyboardLayout { get; set; } = string.Empty;
    public string ExtraArgs { get; set; } = string.Empty;
    public bool BootMenu { get; set; }
    public bool SnapshotMode { get; set; }
    public bool StartPaused { get; set; }
    public List<QEMU选项> QemuOpts { get; set; } = [];
    public List<QEMU设备> Devices { get; set; } = [];
    public List<物理存储挂载> PhysicalStorage { get; set; } = [];
    public int MemoryMb { get; set; } = 4096;
    public int CpuCount { get; set; } = 4;
    public int CpuSockets { get; set; }
    public int CpuCores { get; set; }
    public int CpuThreads { get; set; }
    public int DiskGb { get; set; } = 128;

    [JsonIgnore]
    public bool IsRunning
    {
        get => isRunning;
        set
        {
            if (isRunning == value) return;
            isRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    [JsonIgnore]
    public string StatusText => IsRunning
        ? 语言服务.当前.获取("status.running", "运行中")
        : 语言服务.当前.获取("status.off", "已关机");

    public 仿真配置 Copy() => new()
    {
        Id = Id,
        Name = Name,
        DirPath = DirPath,
        CfgPath = CfgPath,
        DiskPath = DiskPath,
        IsoPath = IsoPath,
        来宾系统 = 来宾系统,
        Arch = Arch,
        Firmware = Firmware,
        MachineType = MachineType,
        Accelerator = Accelerator,
        CpuModel = CpuModel,
        DisplayBackend = DisplayBackend,
        VideoDevice = VideoDevice,
        DisplayWidth = DisplayWidth,
        DisplayHeight = DisplayHeight,
        AudioBackend = AudioBackend,
        AudioDevice = AudioDevice,
        KeyboardDevice = KeyboardDevice,
        MouseDevice = MouseDevice,
        NetworkMode = NetworkMode,
        NetworkModel = NetworkModel,
        NetworkMac = NetworkMac,
        HostForwarding = HostForwarding,
        DiskInterface = DiskInterface,
        DiskCache = DiskCache,
        DiskAio = DiskAio,
        DiskDiscard = DiskDiscard,
        DiskDetectZeroes = DiskDetectZeroes,
        BootOrder = BootOrder,
        BootOnce = BootOnce,
        RtcBase = RtcBase,
        KeyboardLayout = KeyboardLayout,
        ExtraArgs = ExtraArgs,
        BootMenu = BootMenu,
        SnapshotMode = SnapshotMode,
        StartPaused = StartPaused,
        QemuOpts = QemuOpts.Select(option => new QEMU选项 { Name = option.Name, Value = option.Value }).ToList(),
        Devices = Devices.Select(device => new QEMU设备
        {
            Model = device.Model,
            Properties = new Dictionary<string, string>(device.Properties, StringComparer.OrdinalIgnoreCase)
        }).ToList(),
        PhysicalStorage = PhysicalStorage.Select(storage => new 物理存储挂载
        {
            DevicePath = storage.DevicePath,
            DisplayName = storage.DisplayName,
            Interface = storage.Interface,
            ReadOnly = storage.ReadOnly,
            Kind = storage.Kind,
            DiskNumber = storage.DiskNumber,
            PartitionNumber = storage.PartitionNumber,
            Offset = storage.Offset,
            Size = storage.Size,
            UniqueId = storage.UniqueId
        }).ToList(),
        MemoryMb = MemoryMb,
        CpuCount = CpuCount,
        CpuSockets = CpuSockets,
        CpuCores = CpuCores,
        CpuThreads = CpuThreads,
        DiskGb = DiskGb,
        IsRunning = IsRunning
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
