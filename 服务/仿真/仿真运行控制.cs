using System.Text.Json;
using System.Text.Json.Nodes;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU会话
{
    public async Task<(操作结果 结果, string 状态)> 查询运行状态(仿真配置 vm)
    {
        var result = await 执行QMP(vm, "query-status");
        if (!result.Succeeded)
            return (操作结果.Fail(T("session.statusQueryFailed", "无法查询仿真运行状态"), result.Output), string.Empty);
        try
        {
            using var document = JsonDocument.Parse(result.Output);
            var status = document.RootElement.TryGetProperty("status", out var value)
                ? value.GetString() ?? string.Empty
                : string.Empty;
            return (操作结果.Ok(T("session.statusQueried", "已读取仿真运行状态")), status);
        }
        catch (JsonException exception)
        {
            return (操作结果.Fail(T("session.statusQueryFailed", "无法查询仿真运行状态"), exception.Message), string.Empty);
        }
    }

    public Task<操作结果> 暂停(仿真配置 vm) => 执行运行控制(vm, "stop", "qmp.pause", "暂停");

    public Task<操作结果> 继续(仿真配置 vm) => 执行运行控制(vm, "cont", "qmp.resume", "继续");

    public Task<操作结果> 重置(仿真配置 vm) => 执行运行控制(vm, "system_reset", "qmp.reset", "重置");

    public async Task<操作结果> 截取屏幕(仿真配置 vm, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return 操作结果.Fail(T("session.screenshotPathRequired", "截图路径不能为空"));
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? vm.DirPath);
        var arguments = new JsonObject { ["filename"] = path, ["format"] = "png" };
        var result = await 执行QMP(vm, "screendump", arguments.ToJsonString());
        return result.Succeeded
            ? 操作结果.Ok(T("session.screenshotComplete", "截图已保存"))
            : 操作结果.Fail(T("session.screenshotFailed", "截取仿真屏幕失败"), result.Output);
    }

    private async Task<操作结果> 执行运行控制(
        仿真配置 vm,
        string command,
        string nameKey,
        string nameFallback)
    {
        var result = await 执行QMP(vm, command);
        var name = T(nameKey, nameFallback);
        return result.Succeeded
            ? 操作结果.Ok(string.Format(T("session.controlComplete", "{0}已执行"), name))
            : 操作结果.Fail(string.Format(T("session.controlFailed", "{0}失败"), name), result.Output);
    }
}
