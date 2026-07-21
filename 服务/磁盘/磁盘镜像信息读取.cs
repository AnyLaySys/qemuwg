using System.Text.Json;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU镜像
{
    public async Task<磁盘镜像信息?> 获取信息(QEMU安装 install, string path)
    {
        if (!File.Exists(path)) return null;
        var result = await 进程.运行(install.ImgToolPath, ["info", "--output", "json", path]);
        if (result.退出码 != 0) return null;
        try
        {
            using var document = JsonDocument.Parse(result.输出);
            var root = document.RootElement;
            return new 磁盘镜像信息
            {
                Format = GetString(root, "format", "unknown"),
                VirtualSize = GetInt64(root, "virtual-size"),
                ActualSize = GetInt64(root, "actual-size"),
                BackingFile = GetString(root, "backing-filename", string.Empty)
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetString(JsonElement element, string name, string fallback) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static long GetInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : 0;
}
