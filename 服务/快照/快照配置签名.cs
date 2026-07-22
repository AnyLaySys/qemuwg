using System.Security.Cryptography;
using System.Text.Json;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class 快照服务
{
    private static string 计算配置签名(仿真配置 vm)
    {
        var state = new
        {
            vm.Arch,
            vm.Firmware,
            vm.MachineType,
            vm.Accelerator,
            vm.CpuModel,
            vm.MemoryMb,
            vm.CpuCount,
            vm.CpuSockets,
            vm.CpuCores,
            vm.CpuThreads,
            vm.VideoDevice,
            vm.AudioBackend,
            vm.AudioDevice,
            vm.KeyboardDevice,
            vm.MouseDevice,
            vm.NetworkMode,
            vm.NetworkModel,
            vm.NetworkMac,
            vm.DiskInterface,
            vm.IsoPath,
            vm.ExtraArgs,
            QemuOpts = vm.QemuOpts.Select(option => new { option.Name, option.Value }).ToArray(),
            Devices = vm.Devices.Select(device => new
            {
                device.Model,
                Properties = device.Properties.OrderBy(item => item.Key, StringComparer.Ordinal)
            }).ToArray(),
            PhysicalStorage = vm.PhysicalStorage.Select(storage => new
            {
                storage.Interface,
                storage.ReadOnly,
                storage.Kind,
                storage.DiskNumber,
                storage.PartitionNumber,
                storage.Offset,
                storage.Size,
                storage.UniqueId
            }).ToArray()
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state)));
    }
}
