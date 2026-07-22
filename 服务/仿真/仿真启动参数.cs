using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU会话
{
    private IEnumerable<string> BuildArgs(
        QEMU安装 install,
        仿真配置 vm,
        int qmpPort,
        int displayPort,
        IReadOnlyList<string> extraArguments,
        bool requiresOpenGl,
        string loadVmTag)
    {
        var smp = vm.CpuCount.ToString();
        if (vm.CpuSockets > 0 && vm.CpuCores > 0 && vm.CpuThreads > 0
            && vm.CpuSockets * vm.CpuCores * vm.CpuThreads == vm.CpuCount)
            smp = $"cpus={vm.CpuCount},sockets={vm.CpuSockets},cores={vm.CpuCores},threads={vm.CpuThreads}";
        var arguments = new List<string> { "-name", vm.Name, "-m", vm.MemoryMb.ToString(), "-smp", smp };
        if (!string.IsNullOrWhiteSpace(vm.MachineType)) arguments.AddRange(["-machine", vm.MachineType]);
        if (!string.IsNullOrWhiteSpace(vm.Accelerator) && vm.Accelerator != "auto") arguments.AddRange(["-accel", vm.Accelerator]);
        if (!string.IsNullOrWhiteSpace(vm.CpuModel) && vm.CpuModel != "default") arguments.AddRange(["-cpu", vm.CpuModel]);

        if (vm.Firmware == "uefi")
        {
            var code = qemuSvc.查找固件(install, vm.Arch, false);
            if (code is not null)
            {
                arguments.AddRange(["-drive", $"if=pflash,format=raw,readonly=on,file={code}"]);
                var variables = UEFI变量存储.获取路径(vm);
                if (File.Exists(variables))
                    arguments.AddRange(["-drive", $"if=pflash,format=qcow2,file={variables}"]);
            }
        }

        var systemDisk = $"file={vm.DiskPath},format=qcow2,if={RawOrDefault(vm.DiskInterface, "virtio")},id=system-disk";
        if (!是默认值(vm.DiskCache)) systemDisk += $",cache={vm.DiskCache.Trim()}";
        if (!是默认值(vm.DiskAio)) systemDisk += $",aio={vm.DiskAio.Trim()}";
        if (!是默认值(vm.DiskDiscard)) systemDisk += $",discard={vm.DiskDiscard.Trim()}";
        if (!是默认值(vm.DiskDetectZeroes)) systemDisk += $",detect-zeroes={vm.DiskDetectZeroes.Trim()}";
        arguments.AddRange(["-drive", systemDisk]);
        if (File.Exists(vm.IsoPath)) arguments.AddRange(["-drive", $"file={vm.IsoPath},media=cdrom,readonly=on,id=install-media"]);
        var physicalStorage = vm.PhysicalStorage
            .Where(storage => !string.IsNullOrWhiteSpace(storage.DevicePath))
            .ToList();
        var physicalHosts = physicalStorage
            .GroupBy(storage => storage.DevicePath.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select((group, index) => new
            {
                DevicePath = group.Key,
                NodeName = $"physical-host-{index}",
                ReadOnly = group.All(storage => storage.ReadOnly)
            })
            .ToDictionary(item => item.DevicePath, StringComparer.OrdinalIgnoreCase);
        foreach (var host in physicalHosts.Values)
        {
            var readOnly = host.ReadOnly ? "on" : "off";
            arguments.AddRange(["-blockdev", $"driver=host_device,node-name={host.NodeName},filename={host.DevicePath},read-only={readOnly},auto-read-only=off,aio=threads,cache.direct=off,cache.no-flush=off,discard=ignore"]);
        }
        for (var index = 0; index < physicalStorage.Count; index++)
        {
            var storage = physicalStorage[index];
            var hostNode = physicalHosts[storage.DevicePath.Trim()].NodeName;
            var sourceNode = $"physical-source-{index}";
            var readOnly = storage.ReadOnly ? "on" : "off";
            var rawNode = $"driver=raw,node-name={sourceNode},file={hostNode},read-only={readOnly},auto-read-only=off,discard=ignore,detect-zeroes=off";
            if (string.Equals(storage.Kind, "partition", StringComparison.OrdinalIgnoreCase) && storage.Size > 0)
                rawNode += $",offset={storage.Offset},size={storage.Size}";
            arguments.AddRange(["-blockdev", rawNode]);
            var deviceModel = string.Equals(storage.Interface, "ide", StringComparison.OrdinalIgnoreCase)
                ? "ide-hd"
                : "virtio-blk-pci";
            arguments.AddRange(["-device", $"{deviceModel},drive={sourceNode},serial=physical-{storage.DiskNumber}-{storage.PartitionNumber}"]);
        }
        var bootOptions = new List<string>();
        if (!string.IsNullOrWhiteSpace(vm.BootOrder)) bootOptions.Add($"order={vm.BootOrder.Trim()}");
        if (!string.IsNullOrWhiteSpace(vm.BootOnce)) bootOptions.Add($"once={vm.BootOnce.Trim()}");
        if (vm.BootMenu) bootOptions.Add("menu=on");
        if (bootOptions.Count > 0) arguments.AddRange(["-boot", string.Join(',', bootOptions)]);
        if (!string.IsNullOrWhiteSpace(vm.RtcBase)) arguments.AddRange(["-rtc", $"base={vm.RtcBase}"]);
        if (!string.IsNullOrWhiteSpace(vm.KeyboardLayout)) arguments.AddRange(["-k", vm.KeyboardLayout.Trim()]);
        if (vm.SnapshotMode) arguments.Add("-snapshot");
        if (vm.StartPaused) arguments.Add("-S");

        var useDbusDisplay = string.IsNullOrWhiteSpace(vm.DisplayBackend)
                             || string.Equals(vm.DisplayBackend, "dbus", StringComparison.OrdinalIgnoreCase);
        if (useDbusDisplay)
        {
            var dbusDisplay = DBus显示传输.QEMU显示参数;
            if (requiresOpenGl) dbusDisplay = QEMU显示要求.启用OpenGL(dbusDisplay);
            arguments.AddRange(["-display", dbusDisplay]);
        }
        else if (!string.Equals(vm.DisplayBackend, "none", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(vm.DisplayBackend, "vnc", StringComparison.OrdinalIgnoreCase))
                arguments.AddRange(["-vnc", $"127.0.0.1:{displayPort - 5900},share=force-shared"]);
            else
            {
                var displayBackend = vm.DisplayBackend.Trim();
                if (requiresOpenGl) displayBackend = QEMU显示要求.启用OpenGL(displayBackend);
                arguments.AddRange(["-display", displayBackend]);
            }
        }

        var videoDevice = vm.VideoDevice;
        if (!string.IsNullOrWhiteSpace(videoDevice) && videoDevice != "auto")
        {
            videoDevice = 显示分辨率设置.应用(videoDevice, vm.DisplayWidth, vm.DisplayHeight);
            arguments.AddRange(["-device", videoDevice]);
        }
        if (string.Equals(vm.NetworkMode, "none", StringComparison.OrdinalIgnoreCase))
        {
            arguments.AddRange(["-nic", "none"]);
        }
        else if (!string.IsNullOrWhiteSpace(vm.NetworkMode))
        {
            var network = vm.NetworkMode.Trim();
            if (!string.IsNullOrWhiteSpace(vm.NetworkModel) && !string.Equals(vm.NetworkModel, "auto", StringComparison.OrdinalIgnoreCase))
                network += $",model={vm.NetworkModel.Trim()}";
            if (!string.IsNullOrWhiteSpace(vm.NetworkMac)) network += $",mac={vm.NetworkMac.Trim()}";
            if (string.Equals(vm.NetworkMode, "user", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var rule in vm.HostForwarding.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    network += $",hostfwd={rule}";
            }
            arguments.AddRange(["-nic", network]);
        }
        if (!string.IsNullOrWhiteSpace(vm.AudioDevice) && vm.AudioDevice != "auto")
        {
            var audioDriver = string.IsNullOrWhiteSpace(vm.AudioBackend) ? "none" : vm.AudioBackend.Trim();
            arguments.AddRange(["-audio", $"driver={audioDriver},model={vm.AudioDevice.Trim()}"]);
        }
        else if (vm.AudioBackend == "none") arguments.AddRange(["-audiodev", "driver=none,id=audio0"]);
        else if (!string.IsNullOrWhiteSpace(vm.AudioBackend)) arguments.AddRange(["-audiodev", $"driver={vm.AudioBackend},id=audio0"]);
        if (!string.IsNullOrWhiteSpace(vm.KeyboardDevice)
            && !string.Equals(vm.KeyboardDevice, "auto", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(vm.KeyboardDevice, "none", StringComparison.OrdinalIgnoreCase))
            arguments.AddRange(["-device", vm.KeyboardDevice.Trim()]);
        if (!string.IsNullOrWhiteSpace(vm.MouseDevice)
            && !string.Equals(vm.MouseDevice, "auto", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(vm.MouseDevice, "none", StringComparison.OrdinalIgnoreCase))
            arguments.AddRange(["-device", vm.MouseDevice.Trim()]);

        foreach (var device in vm.Devices.Where(device => !string.IsNullOrWhiteSpace(device.Model)))
        {
            var value = device.Model.Trim();
            if (device.Properties.Count > 0)
                value += "," + string.Join(',', device.Properties
                    .Where(item => !string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
                    .Select(item => $"{item.Key.Trim()}={item.Value.Trim()}"));
            arguments.AddRange(["-device", value]);
        }

        if (!string.IsNullOrWhiteSpace(loadVmTag))
            arguments.AddRange(["-loadvm", loadVmTag.Trim()]);

        arguments.AddRange(["-qmp", $"tcp:127.0.0.1:{qmpPort},server=on,wait=off"]);
        foreach (var option in vm.QemuOpts)
        {
            var name = option.Name.Trim();
            if (name.Length == 0) continue;
            arguments.Add(name.StartsWith('-') ? name : "-" + name);
            if (!string.IsNullOrWhiteSpace(option.Value))
            {
                var value = option.Value.Trim();
                if (requiresOpenGl && QEMU显示要求.是显示选项(name))
                    value = QEMU显示要求.启用OpenGL(value);
                arguments.Add(value);
            }
        }
        arguments.AddRange(extraArguments);
        return arguments;
    }

    private static bool 是默认值(string value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value, "default", StringComparison.OrdinalIgnoreCase);

    private static string RawOrDefault(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
