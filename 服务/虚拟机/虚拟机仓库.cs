using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed class 虚拟机仓库
{
    private static string T(string key, string fallback) => 语言服务.当前.获取(key, fallback);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public 虚拟机仓库()
    {
        根目录 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "qemuwg", "vm");
        Directory.CreateDirectory(根目录);
    }

    public string 根目录 { get; }

    public async Task<IReadOnlyList<虚拟机配置>> 加载全部()
    {
        return await Task.Run(async () =>
        {
            var result = new List<虚拟机配置>();
            foreach (var path in Directory.EnumerateFiles(根目录, "*.qemu", System.IO.SearchOption.AllDirectories))
            {
                try
                {
                    await using var stream = File.OpenRead(path);
                    var vm = await JsonSerializer.DeserializeAsync<虚拟机配置>(stream, JsonOptions);
                    if (vm is null) continue;
                    vm.CfgPath = path;
                    vm.DirPath = Path.GetDirectoryName(path) ?? string.Empty;
                    迁移输入设备(vm);
                    result.Add(vm);
                }
                catch (JsonException)
                {
                }
            }
            return result.OrderBy(vm => vm.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        });
    }

    public async Task<(操作结果 结果, 虚拟机配置? 虚拟机)> 创建(
        QEMU安装 qemu,
        虚拟机配置 request,
        string parentDirectory)
    {
        if (!qemu.IsAvailable || !File.Exists(qemu.ImgToolPath))
            return (操作结果.Fail(T("repo.imgMissing", "未找到 qemu-img")), null);

        var safeName = 清理名称(request.Name);
        var directory = 获取唯一目录(parentDirectory, safeName);
        Directory.CreateDirectory(directory);

        var vm = request.Copy();
        vm.Id = Guid.NewGuid().ToString("N");
        vm.Name = safeName;
        vm.DirPath = directory;
        vm.CfgPath = Path.Combine(directory, safeName + ".qemu");
        vm.DiskPath = Path.Combine(directory, "system.qcow2");

        var disk = await 进程.运行(qemu.ImgToolPath,
            ["create", "-f", "qcow2", vm.DiskPath, $"{vm.DiskGb}G"]);
        if (disk.退出码 != 0)
        {
            尝试删除空目录(directory);
            return (操作结果.Fail(T("repo.diskCreateFailed", "创建虚拟磁盘失败"), disk.输出), null);
        }

        var saved = await 保存(vm);
        if (!saved.Succeeded)
        {
            尝试删除目录(directory);
            return (saved, null);
        }
        return (操作结果.Ok(T("repo.created", "虚拟机已创建")), vm);
    }

    public async Task<操作结果> 更新(虚拟机配置 vm)
    {
        if (!是受管配置(vm.CfgPath)) return 操作结果.Fail(T("repo.unmanagedConfig", "配置文件不在 QemuWG 虚拟机库中"));
        return await 保存(vm);
    }

    public Task<操作结果> 删除(虚拟机配置 vm) => Task.Run(() =>
    {
        if (!是受管配置(vm.CfgPath) || !Directory.Exists(vm.DirPath))
            return 操作结果.Fail(T("repo.invalidDirectory", "虚拟机目录无效"));
        try
        {
            FileSystem.DeleteDirectory(vm.DirPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            return 操作结果.Ok(T("repo.recycled", "虚拟机已移至回收站"));
        }
        catch (Exception exception)
        {
            return 操作结果.Fail(T("repo.deleteFailed", "删除虚拟机失败"), exception.Message);
        }
    });

    private static async Task<操作结果> 保存(虚拟机配置 vm)
    {
        try
        {
            Directory.CreateDirectory(vm.DirPath);
            var temporaryPath = vm.CfgPath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
                await JsonSerializer.SerializeAsync(stream, vm, JsonOptions);
            File.Move(temporaryPath, vm.CfgPath, true);
            return 操作结果.Ok(T("repo.saved", "配置已保存"));
        }
        catch (Exception exception)
        {
            return 操作结果.Fail(T("repo.saveFailed", "保存配置失败"), exception.Message);
        }
    }

    private bool 是受管配置(string configPath)
    {
        if (!string.Equals(Path.GetExtension(configPath), ".qemu", StringComparison.OrdinalIgnoreCase)) return false;
        var root = Path.GetFullPath(根目录).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var config = Path.GetFullPath(configPath);
        return config.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string 获取唯一目录(string parent, string name)
    {
        Directory.CreateDirectory(parent);
        var candidate = Path.Combine(parent, name);
        for (var suffix = 2; Directory.Exists(candidate); suffix++) candidate = Path.Combine(parent, $"{name} ({suffix})");
        return candidate;
    }

    private static string 清理名称(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Trim().Select(character => invalid.Contains(character) || char.IsControl(character) ? '_' : character).ToArray())
            .TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(sanitized) ? T("repo.defaultName", "新建虚拟机") : sanitized;
    }

    private static void 迁移输入设备(虚拟机配置 vm)
    {
        var migrateKeyboard = string.IsNullOrWhiteSpace(vm.KeyboardDevice)
                              || string.Equals(vm.KeyboardDevice, "auto", StringComparison.OrdinalIgnoreCase);
        var migrateMouse = string.IsNullOrWhiteSpace(vm.MouseDevice)
                           || string.Equals(vm.MouseDevice, "auto", StringComparison.OrdinalIgnoreCase);
        foreach (var device in vm.Devices.ToList())
        {
            if (migrateKeyboard && QEMU服务.是键盘设备(device.Model))
            {
                vm.KeyboardDevice = device.Model;
                vm.Devices.Remove(device);
                migrateKeyboard = false;
            }
            else if (migrateMouse && QEMU服务.是指针设备(device.Model))
            {
                vm.MouseDevice = device.Model;
                vm.Devices.Remove(device);
                migrateMouse = false;
            }
        }
    }

    private static void 尝试删除空目录(string path)
    {
        try { Directory.Delete(path, false); } catch { }
    }

    private static void 尝试删除目录(string path)
    {
        try { Directory.Delete(path, true); } catch { }
    }
}
