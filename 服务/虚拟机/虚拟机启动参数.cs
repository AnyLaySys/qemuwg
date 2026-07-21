using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU会话
{
    private static readonly HashSet<string> GuestAgentArchs = new(StringComparer.OrdinalIgnoreCase)
    {
        "aarch64", "alpha", "arm", "hppa", "i386", "loongarch64", "m68k", "mips", "mips64",
        "mips64el", "mipsel", "or1k", "ppc", "ppc64", "riscv32", "riscv64", "s390x", "sh4", "x86_64"
    };

    private IEnumerable<string> BuildArgs(
        QEMU安装 install,
        虚拟机配置 vm,
        int qmpPort,
        int guestAgentPort,
        int displayPort,
        IReadOnlyList<string> extraArguments,
        bool requiresOpenGl)
    {
        var arguments = new List<string> { "-name", vm.Name, "-m", vm.MemoryMb.ToString(), "-smp", vm.CpuCount.ToString() };
        if (!string.IsNullOrWhiteSpace(vm.MachineType)) arguments.AddRange(["-machine", vm.MachineType]);
        if (!string.IsNullOrWhiteSpace(vm.Accelerator) && vm.Accelerator != "auto") arguments.AddRange(["-accel", vm.Accelerator]);
        if (!string.IsNullOrWhiteSpace(vm.CpuModel) && vm.CpuModel != "default") arguments.AddRange(["-cpu", vm.CpuModel]);

        if (vm.Firmware == "uefi")
        {
            var code = qemuSvc.查找固件(install, vm.Arch, false);
            var variablesTemplate = qemuSvc.查找固件(install, vm.Arch, true);
            if (code is not null)
            {
                arguments.AddRange(["-drive", $"if=pflash,format=raw,readonly=on,file={code}"]);
                if (variablesTemplate is not null)
                {
                    var variables = Path.Combine(vm.DirPath, "uefi-vars.fd");
                    if (!File.Exists(variables)) File.Copy(variablesTemplate, variables);
                    arguments.AddRange(["-drive", $"if=pflash,format=raw,file={variables}"]);
                }
            }
        }

        arguments.AddRange(["-drive", $"file={vm.DiskPath},format=qcow2,if=virtio,id=system-disk"]);
        if (File.Exists(vm.IsoPath)) arguments.AddRange(["-drive", $"file={vm.IsoPath},media=cdrom,readonly=on,id=install-media"]);
        if (!string.IsNullOrWhiteSpace(vm.BootOrder)) arguments.AddRange(["-boot", $"order={vm.BootOrder}"]);
        if (!string.IsNullOrWhiteSpace(vm.RtcBase)) arguments.AddRange(["-rtc", $"base={vm.RtcBase}"]);

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
        if (!string.IsNullOrWhiteSpace(videoDevice) && videoDevice != "auto") arguments.AddRange(["-device", videoDevice]);
        if (string.Equals(vm.NetworkMode, "none", StringComparison.OrdinalIgnoreCase))
        {
            arguments.AddRange(["-nic", "none"]);
        }
        else if (!string.IsNullOrWhiteSpace(vm.NetworkMode))
        {
            var network = vm.NetworkMode.Trim();
            if (!string.IsNullOrWhiteSpace(vm.NetworkModel) && !string.Equals(vm.NetworkModel, "auto", StringComparison.OrdinalIgnoreCase))
                network += $",model={vm.NetworkModel.Trim()}";
            arguments.AddRange(["-nic", network]);
        }
        if (vm.AudioBackend == "none") arguments.AddRange(["-audiodev", "driver=none,id=audio0"]);
        else if (!string.IsNullOrWhiteSpace(vm.AudioBackend)) arguments.AddRange(["-audiodev", $"driver={vm.AudioBackend},id=audio0"]);
        if (!string.IsNullOrWhiteSpace(vm.AudioDevice) && vm.AudioDevice != "auto")
            arguments.AddRange(["-device", vm.AudioDevice]);

        foreach (var device in vm.Devices.Where(device => !string.IsNullOrWhiteSpace(device.Model)))
        {
            var value = device.Model.Trim();
            if (device.Properties.Count > 0)
                value += "," + string.Join(',', device.Properties
                    .Where(item => !string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
                    .Select(item => $"{item.Key.Trim()}={item.Value.Trim()}"));
            arguments.AddRange(["-device", value]);
        }

        arguments.AddRange(["-qmp", $"tcp:127.0.0.1:{qmpPort},server=on,wait=off"]);
        if (vm.EnableGuestAgent && GuestAgentArchs.Contains(vm.Arch))
        {
            arguments.AddRange([
                "-device", "virtio-serial",
                "-chardev", $"socket,id=qga0,host=127.0.0.1,port={guestAgentPort},server=on,wait=off",
                "-device", "virtserialport,chardev=qga0,name=org.qemu.guest_agent.0"
            ]);
        }
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
}
