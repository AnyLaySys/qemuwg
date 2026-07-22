namespace QemuWG.数据;

public sealed class QEMU能力
{
    public IReadOnlyList<string> Machines { get; init; } = [];
    public IReadOnlyList<string> CpuModels { get; init; } = [];
    public IReadOnlyList<string> Accelerators { get; init; } = [];
    public IReadOnlyList<string> DisplayBackends { get; init; } = [];
    public IReadOnlyList<string> VideoDevices { get; init; } = [];
    public IReadOnlyList<string> NetworkBackends { get; init; } = [];
    public IReadOnlyList<string> NetworkDevices { get; init; } = [];
    public IReadOnlyList<string> AudioBackends { get; init; } = [];
    public IReadOnlyList<string> AudioDevices { get; init; } = [];
    public IReadOnlyList<string> AudioModels { get; init; } = [];
    public IReadOnlyList<string> InputDevices { get; init; } = [];
    public IReadOnlyList<string> KeyboardDevices { get; init; } = [];
    public IReadOnlyList<string> PointerDevices { get; init; } = [];
    public IReadOnlyList<string> KeyboardLayouts { get; init; } = [];
    public IReadOnlyList<string> AllDevices { get; init; } = [];
    public IReadOnlyList<QEMU命令选项> CmdOptions { get; init; } = [];
}
