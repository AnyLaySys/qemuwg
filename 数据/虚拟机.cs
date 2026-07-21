using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using QemuWG.服务;

namespace QemuWG.数据;

public sealed class 虚拟机配置 : INotifyPropertyChanged
{
    private bool isRunning;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Windows";
    public string DirPath { get; set; } = string.Empty;
    public string CfgPath { get; set; } = string.Empty;
    public string DiskPath { get; set; } = string.Empty;
    public string IsoPath { get; set; } = string.Empty;
    public string Arch { get; set; } = "x86_64";
    public string Firmware { get; set; } = "uefi";
    public string MachineType { get; set; } = string.Empty;
    public string Accelerator { get; set; } = "tcg";
    public string CpuModel { get; set; } = "default";
    public string DisplayBackend { get; set; } = "vnc";
    public string VideoDevice { get; set; } = "auto";
    public string AudioBackend { get; set; } = "none";
    public string AudioDevice { get; set; } = "auto";
    public string NetworkMode { get; set; } = "user";
    public string NetworkModel { get; set; } = "auto";
    public string BootOrder { get; set; } = "dc";
    public string RtcBase { get; set; } = "localtime";
    public string ExtraArgs { get; set; } = string.Empty;
    public bool EnableGuestAgent { get; set; } = true;
    public List<QEMU选项> QemuOpts { get; set; } = [];
    public List<QEMU设备> Devices { get; set; } = [];
    public int MemoryMb { get; set; } = 4096;
    public int CpuCount { get; set; } = 4;
    public int DiskGb { get; set; } = 64;

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
        ? 语言服务.Current.Get("status.running", "运行中")
        : 语言服务.Current.Get("status.off", "已关机");

    public 虚拟机配置 Copy() => new()
    {
        Id = Id,
        Name = Name,
        DirPath = DirPath,
        CfgPath = CfgPath,
        DiskPath = DiskPath,
        IsoPath = IsoPath,
        Arch = Arch,
        Firmware = Firmware,
        MachineType = MachineType,
        Accelerator = Accelerator,
        CpuModel = CpuModel,
        DisplayBackend = DisplayBackend,
        VideoDevice = VideoDevice,
        AudioBackend = AudioBackend,
        AudioDevice = AudioDevice,
        NetworkMode = NetworkMode,
        NetworkModel = NetworkModel,
        BootOrder = BootOrder,
        RtcBase = RtcBase,
        ExtraArgs = ExtraArgs,
        EnableGuestAgent = EnableGuestAgent,
        QemuOpts = QemuOpts.Select(option => new QEMU选项 { Name = option.Name, Value = option.Value }).ToList(),
        Devices = Devices.Select(device => new QEMU设备
        {
            Model = device.Model,
            Properties = new Dictionary<string, string>(device.Properties, StringComparer.OrdinalIgnoreCase)
        }).ToList(),
        MemoryMb = MemoryMb,
        CpuCount = CpuCount,
        DiskGb = DiskGb,
        IsRunning = IsRunning
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
