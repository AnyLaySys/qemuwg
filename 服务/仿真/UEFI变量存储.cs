using QemuWG.数据;

namespace QemuWG.服务;

internal static class UEFI变量存储
{
    private const string 旧变量文件名 = "uefi-vars.fd";
    private const string 变量镜像文件名 = "uefi-vars.qcow2";

    public static string 获取路径(仿真配置 仿真) => Path.Combine(仿真.DirPath, 变量镜像文件名);

    public static 操作结果 准备(QEMU安装 安装, 仿真配置 仿真, string? 变量模板)
    {
        var 目标路径 = 获取路径(仿真);
        if (File.Exists(目标路径)) return 操作结果.Ok(目标路径);
        if (!File.Exists(安装.ImgToolPath))
            return 操作结果.Fail(
                语言服务.当前.获取("repo.imgMissing", "未找到 qemu-img"),
                安装.ImgToolPath);

        var 旧变量路径 = Path.Combine(仿真.DirPath, 旧变量文件名);
        var 源路径 = File.Exists(旧变量路径)
            ? 旧变量路径
            : File.Exists(变量模板) ? 变量模板 : null;
        if (源路径 is null)
            return 操作结果.Fail(
                语言服务.当前.获取("session.uefiVariablesMissing", "找不到 UEFI 变量模板。"));

        Directory.CreateDirectory(仿真.DirPath);
        var 临时路径 = 目标路径 + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var 结果 = 进程.运行(安装.ImgToolPath,
                    ["convert", "-f", "raw", "-O", "qcow2", 源路径, 临时路径])
                .GetAwaiter().GetResult();
            if (结果.退出码 != 0)
                return 操作结果.Fail(
                    语言服务.当前.获取("session.uefiVariablesConvertFailed", "无法准备 UEFI 变量存储。"),
                    结果.输出);

            File.Move(临时路径, 目标路径, false);
            return 操作结果.Ok(目标路径);
        }
        catch (Exception exception)
        {
            return 操作结果.Fail(
                语言服务.当前.获取("session.uefiVariablesConvertFailed", "无法准备 UEFI 变量存储。"),
                exception.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(临时路径)) File.Delete(临时路径);
            }
            catch
            {
            }
        }
    }
}
