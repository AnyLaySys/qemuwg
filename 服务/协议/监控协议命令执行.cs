using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using QemuWG.数据;

namespace QemuWG.服务;

public sealed partial class QEMU会话
{
    public async Task<QMP结果> 执行QMP(
        虚拟机配置 vm,
        string command,
        string argumentsJson = "",
        CancellationToken cancellationToken = default)
    {
        var results = await 执行QMP批次(
            vm,
            [new QMP请求(command, argumentsJson)],
            cancellationToken);
        return results[0];
    }

    public async Task<IReadOnlyList<QMP结果>> 执行QMP批次(
        虚拟机配置 vm,
        IReadOnlyList<QMP请求> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0) return [];

        Session? session;
        lock (gate) sessions.TryGetValue(vm.Id, out session);
        if (session is null || !session.IsActive)
            return [new QMP结果(false, T("session.notRunning", "虚拟机没有运行"), "NotRunning")];

        try
        {
            var preparedRequests = new JsonObject[requests.Count];
            for (var index = 0; index < requests.Count; index++)
            {
                var source = requests[index];
                if (string.IsNullOrWhiteSpace(source.命令))
                    return [new QMP结果(false, T("qmp.commandRequired", "QMP 命令不能为空。"), "InvalidCommand")];

                var request = new JsonObject { ["execute"] = source.命令 };
                if (!string.IsNullOrWhiteSpace(source.参数))
                {
                    var arguments = JsonNode.Parse(source.参数);
                    if (arguments is not JsonObject)
                        return [new QMP结果(false, T("qmp.argumentsObject", "QMP 参数必须是 JSON 对象。"), "InvalidArguments")];
                    request["arguments"] = arguments;
                }
                preparedRequests[index] = request;
            }

            using var timeoutCancellation = new CancellationTokenSource(ProtocolTimeout);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, session.Lifetime.Token, timeoutCancellation.Token);
            var sessionToken = linkedCancellation.Token;
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, session.QmpPort, sessionToken);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            await 读取JSON行(reader, sessionToken);

            await 写入QMP(stream, "{\"execute\":\"qmp_capabilities\"}\r\n", sessionToken);
            var caps = await 读取QMP响应(reader, sessionToken);
            if (!caps.Succeeded) return [caps];

            var results = new List<QMP结果>(preparedRequests.Length);
            foreach (var request in preparedRequests)
            {
                await 写入QMP(stream, request.ToJsonString() + "\r\n", sessionToken);
                var result = await 读取QMP响应(reader, sessionToken);
                results.Add(result);
                if (!result.Succeeded) break;
            }
            return results;
        }
        catch (OperationCanceledException) when (session.Lifetime.IsCancellationRequested)
        {
            return [new QMP结果(false, T("qmp.connectionClosed", "QMP 连接已关闭。"), "ConnectionClosed")];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [new QMP结果(false, T("qmp.timeout", "QMP 操作超时，请确认虚拟机仍在响应。"), "Timeout")];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return [new QMP结果(false, exception.Message, exception.GetType().Name)];
        }
    }
}
