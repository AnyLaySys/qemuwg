using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU会话
{
    public async Task<来宾代理结果> 执行来宾代理(
        虚拟机配置 vm,
        string command,
        string argumentsJson = "",
        CancellationToken cancellationToken = default)
    {
        Session? session;
        lock (gate) sessions.TryGetValue(vm.Id, out session);
        if (session is null || !session.IsActive)
            return new 来宾代理结果(false, T("session.notRunning", "虚拟机没有运行"), "NotRunning");

        try
        {
            using var timeoutCancellation = new CancellationTokenSource(ProtocolTimeout);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, session.Lifetime.Token, timeoutCancellation.Token);
            var sessionToken = linkedCancellation.Token;
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, session.GuestAgentPort, sessionToken);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var request = new JsonObject { ["execute"] = command };
            if (!string.IsNullOrWhiteSpace(argumentsJson))
            {
                var arguments = JsonNode.Parse(argumentsJson);
                if (arguments is not JsonObject)
                    return new 来宾代理结果(false, T("guestAgent.argumentsObject", "Guest Agent 参数必须是 JSON 对象。"), "InvalidArguments");
                request["arguments"] = arguments;
            }

            await 写入QMP(stream, request.ToJsonString() + "\r\n", sessionToken);
            while (true)
            {
                var line = await 读取JSON行(reader, sessionToken);
                using var document = JsonDocument.Parse(line.TrimStart('\u00ff'));
                var root = document.RootElement;
                if (root.TryGetProperty("return", out var returned))
                    return new 来宾代理结果(true, JsonSerializer.Serialize(returned, new JsonSerializerOptions { WriteIndented = true }));
                if (root.TryGetProperty("error", out var error))
                {
                    var errorClass = error.TryGetProperty("class", out var value) ? value.GetString() ?? "GuestAgentError" : "GuestAgentError";
                    return new 来宾代理结果(false, JsonSerializer.Serialize(error, new JsonSerializerOptions { WriteIndented = true }), errorClass);
                }
            }
        }
        catch (OperationCanceledException) when (session.Lifetime.IsCancellationRequested)
        {
            return new 来宾代理结果(false, T("guestAgent.unavailable", "Guest Agent 未连接或未安装。"), "ConnectionClosed");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new 来宾代理结果(false, T("guestAgent.timeout", "Guest Agent 操作超时。"), "Timeout");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new 来宾代理结果(false, T("guestAgent.unavailable", "Guest Agent 未连接或未安装。") + Environment.NewLine + exception.Message, exception.GetType().Name);
        }
    }
}
