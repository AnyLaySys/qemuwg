using System.Text.Json;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU服务
{
    private static readonly JsonSerializerOptions 物理存储JSON选项 = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<物理存储设备>> 获取物理存储()
    {
        const string script = """
            $ErrorActionPreference = 'Stop'
            $items = [System.Collections.Generic.List[object]]::new()
            $namespace = 'root/Microsoft/Windows/Storage'
            $disks = @(Get-CimInstance -Namespace $namespace -ClassName MSFT_Disk)
            $partitions = @(Get-CimInstance -Namespace $namespace -ClassName MSFT_Partition)
            foreach ($disk in $disks) {
                $items.Add([pscustomobject]@{
                    DevicePath = "\\.\PhysicalDrive$($disk.Number)"
                    FriendlyName = [string]$disk.FriendlyName
                    Kind = 'disk'
                    DiskNumber = [int]$disk.Number
                    PartitionNumber = 0
                    Size = [long]$disk.Size
                    Offset = 0
                    PartitionType = [string]$disk.PartitionStyle
                    UniqueId = [string]$disk.UniqueId
                    IsBoot = [bool]$disk.IsBoot
                    IsSystem = [bool]$disk.IsSystem
                })
                foreach ($partition in $partitions) {
                    if ([int]$partition.DiskNumber -ne [int]$disk.Number) { continue }
                    $items.Add([pscustomobject]@{
                        DevicePath = "\\.\PhysicalDrive$($disk.Number)"
                        FriendlyName = [string]$disk.FriendlyName
                        Kind = 'partition'
                        DiskNumber = [int]$disk.Number
                        PartitionNumber = [int]$partition.PartitionNumber
                        Size = [long]$partition.Size
                        Offset = [long]$partition.Offset
                        PartitionType = [string]$partition.Type
                        UniqueId = [string]$disk.UniqueId
                        IsBoot = [bool]$disk.IsBoot
                        IsSystem = [bool]$disk.IsSystem
                    })
                }
            }
            ConvertTo-Json -InputObject $items -Compress
            """;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        进程结果 result;
        try
        {
            result = await 进程.运行("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], timeout.Token);
        }
        catch (OperationCanceledException)
        {
            应用日志.写("Physical storage scan timed out.");
            return [];
        }
        if (result.退出码 != 0 || string.IsNullOrWhiteSpace(result.输出)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<物理存储设备>>(result.输出, 物理存储JSON选项) ?? [];
        }
        catch (JsonException exception)
        {
            应用日志.写("Physical storage scan failed: " + exception.Message);
            return [];
        }
    }
}
