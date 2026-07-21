using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed class 虚拟机仓库
{
    private static string T(string key, string fallback) => 语言服务.Current.Get(key, fallback);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public 虚拟机仓库()
    {
        RootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "qemuwg", "vm");
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public async Task<IReadOnlyList<虚拟机配置>> LoadAllAsync()
    {
        return await Task.Run(async () =>
        {
            var result = new List<虚拟机配置>();
            foreach (var path in Directory.EnumerateFiles(RootPath, "*.qemu", System.IO.SearchOption.AllDirectories))
            {
                try
                {
                    await using var stream = File.OpenRead(path);
                    var vm = await JsonSerializer.DeserializeAsync<虚拟机配置>(stream, JsonOptions);
                    if (vm is null) continue;
                    vm.DisplayBackend = "vnc";
                    vm.CfgPath = path;
                    vm.DirPath = Path.GetDirectoryName(path) ?? string.Empty;
                    result.Add(vm);
                }
                catch (JsonException)
                {
                }
            }
            return result.OrderBy(vm => vm.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        });
    }

    public async Task<(操作结果 Result, 虚拟机配置? Machine)> CreateAsync(
        QEMU安装 qemu,
        虚拟机配置 request,
        string parentDirectory)
    {
        if (!qemu.IsAvailable || !File.Exists(qemu.ImgToolPath))
            return (操作结果.Fail(T("repo.imgMissing", "未找到 qemu-img")), null);

        var safeName = SanitizeName(request.Name);
        var directory = GetUniqueDirectory(parentDirectory, safeName);
        Directory.CreateDirectory(directory);

        var vm = request.Copy();
        vm.Id = Guid.NewGuid().ToString("N");
        vm.Name = safeName;
        vm.DirPath = directory;
        vm.CfgPath = Path.Combine(directory, safeName + ".qemu");
        vm.DiskPath = Path.Combine(directory, "system.qcow2");

        var disk = await 进程.RunAsync(qemu.ImgToolPath,
            ["create", "-f", "qcow2", vm.DiskPath, $"{vm.DiskGb}G"]);
        if (disk.ExitCode != 0)
        {
            TryDeleteEmptyDirectory(directory);
            return (操作结果.Fail(T("repo.diskCreateFailed", "创建虚拟磁盘失败"), disk.Output), null);
        }

        var saved = await SaveAsync(vm);
        if (!saved.Succeeded)
        {
            TryDeleteDirectory(directory);
            return (saved, null);
        }
        return (操作结果.Ok(T("repo.created", "虚拟机已创建")), vm);
    }

    public async Task<操作结果> UpdateAsync(虚拟机配置 vm)
    {
        if (!IsManagedConfig(vm.CfgPath)) return 操作结果.Fail(T("repo.unmanagedConfig", "配置文件不在 QemuWG 虚拟机库中"));
        return await SaveAsync(vm);
    }

    public Task<操作结果> DeleteAsync(虚拟机配置 vm) => Task.Run(() =>
    {
        if (!IsManagedConfig(vm.CfgPath) || !Directory.Exists(vm.DirPath))
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

    private static async Task<操作结果> SaveAsync(虚拟机配置 vm)
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

    private bool IsManagedConfig(string configPath)
    {
        if (!string.Equals(Path.GetExtension(configPath), ".qemu", StringComparison.OrdinalIgnoreCase)) return false;
        var root = Path.GetFullPath(RootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var config = Path.GetFullPath(configPath);
        return config.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUniqueDirectory(string parent, string name)
    {
        Directory.CreateDirectory(parent);
        var candidate = Path.Combine(parent, name);
        for (var suffix = 2; Directory.Exists(candidate); suffix++) candidate = Path.Combine(parent, $"{name} ({suffix})");
        return candidate;
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Trim().Select(character => invalid.Contains(character) || char.IsControl(character) ? '_' : character).ToArray())
            .TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(sanitized) ? T("repo.defaultName", "新建虚拟机") : sanitized;
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try { Directory.Delete(path, false); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, true); } catch { }
    }
}

