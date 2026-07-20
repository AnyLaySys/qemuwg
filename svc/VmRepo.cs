using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using QemuWG.data;

namespace QemuWG.svc;

public sealed class VmRepo
{
    private static string T(string key, string fallback) => LocaleSvc.Current.Get(key, fallback);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public VmRepo()
    {
        RootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "qemuwg", "vm");
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public async Task<IReadOnlyList<VmCfg>> LoadAllAsync()
    {
        return await Task.Run(async () =>
        {
            var result = new List<VmCfg>();
            foreach (var path in Directory.EnumerateFiles(RootPath, "*.qemu", System.IO.SearchOption.AllDirectories))
            {
                try
                {
                    await using var stream = File.OpenRead(path);
                    var vm = await JsonSerializer.DeserializeAsync<VmCfg>(stream, JsonOptions);
                    if (vm is null) continue;
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

    public async Task<(OperationResult Result, VmCfg? Machine)> CreateAsync(
        QemuInstall qemu,
        VmCfg request,
        string parentDirectory)
    {
        if (!qemu.IsAvailable || !File.Exists(qemu.ImgToolPath))
            return (OperationResult.Fail(T("repo.imgMissing", "未找到 qemu-img")), null);

        var safeName = SanitizeName(request.Name);
        var directory = GetUniqueDirectory(parentDirectory, safeName);
        Directory.CreateDirectory(directory);

        var vm = request.Copy();
        vm.Id = Guid.NewGuid().ToString("N");
        vm.Name = safeName;
        vm.DirPath = directory;
        vm.CfgPath = Path.Combine(directory, safeName + ".qemu");
        vm.DiskPath = Path.Combine(directory, "system.qcow2");

        var disk = await ProcessRunner.RunAsync(qemu.ImgToolPath,
            ["create", "-f", "qcow2", vm.DiskPath, $"{vm.DiskGb}G"]);
        if (disk.ExitCode != 0)
        {
            TryDeleteEmptyDirectory(directory);
            return (OperationResult.Fail(T("repo.diskCreateFailed", "创建虚拟磁盘失败"), disk.Output), null);
        }

        var saved = await SaveAsync(vm);
        if (!saved.Succeeded)
        {
            TryDeleteDirectory(directory);
            return (saved, null);
        }
        return (OperationResult.Ok(T("repo.created", "虚拟机已创建")), vm);
    }

    public async Task<OperationResult> UpdateAsync(VmCfg vm)
    {
        if (!IsManagedConfig(vm.CfgPath)) return OperationResult.Fail(T("repo.unmanagedConfig", "配置文件不在 QemuWG 虚拟机库中"));
        return await SaveAsync(vm);
    }

    public Task<OperationResult> DeleteAsync(VmCfg vm) => Task.Run(() =>
    {
        if (!IsManagedConfig(vm.CfgPath) || !Directory.Exists(vm.DirPath))
            return OperationResult.Fail(T("repo.invalidDirectory", "虚拟机目录无效"));
        try
        {
            FileSystem.DeleteDirectory(vm.DirPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            return OperationResult.Ok(T("repo.recycled", "虚拟机已移至回收站"));
        }
        catch (Exception exception)
        {
            return OperationResult.Fail(T("repo.deleteFailed", "删除虚拟机失败"), exception.Message);
        }
    });

    private static async Task<OperationResult> SaveAsync(VmCfg vm)
    {
        try
        {
            Directory.CreateDirectory(vm.DirPath);
            var temporaryPath = vm.CfgPath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
                await JsonSerializer.SerializeAsync(stream, vm, JsonOptions);
            File.Move(temporaryPath, vm.CfgPath, true);
            return OperationResult.Ok(T("repo.saved", "配置已保存"));
        }
        catch (Exception exception)
        {
            return OperationResult.Fail(T("repo.saveFailed", "保存配置失败"), exception.Message);
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




