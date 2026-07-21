using System.Diagnostics;
using System.Text;

namespace QemuWG.服务;

public sealed record 进程结果(int 退出码, string 输出);

public static class 进程
{
    public static async Task<进程结果> 运行(
        string executable,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) return new 进程结果(-1, 语言服务.当前.获取("process.startFailed", "无法启动进程"));

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        var output = (await outputTask) + (await errorTask);
        return new 进程结果(process.ExitCode, output.Trim());
    }
}
