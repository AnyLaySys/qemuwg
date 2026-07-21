using System.Text;
using System.Text.Json;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU会话
{
    private static readonly TimeSpan ProtocolTimeout = TimeSpan.FromSeconds(5);

    private static async Task 写入QMP(Stream stream, string command, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(command);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<QMP结果> 读取QMP响应(StreamReader reader, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await 读取JSON行(reader, cancellationToken);
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("return", out var returned))
                return new QMP结果(true, JsonSerializer.Serialize(returned, new JsonSerializerOptions { WriteIndented = true }));
            if (root.TryGetProperty("error", out var error))
            {
                var errorClass = error.TryGetProperty("class", out var value) ? value.GetString() ?? "QmpError" : "QmpError";
                return new QMP结果(false, JsonSerializer.Serialize(error, new JsonSerializerOptions { WriteIndented = true }), errorClass);
            }
        }
    }

    private static async Task<string> 读取JSON行(StreamReader reader, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) throw new EndOfStreamException(语言服务.当前.获取("qmp.connectionClosed", "QMP 连接已关闭。"));
            if (!string.IsNullOrWhiteSpace(line)) return line;
        }
    }
}
