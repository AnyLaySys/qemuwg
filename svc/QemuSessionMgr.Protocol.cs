using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using QemuWG.data;

namespace QemuWG.svc;

public sealed partial class QemuSessionMgr
{
    public async Task<QmpResult> ExecuteQmpAsync(
        VmCfg vm,
        string command,
        string argumentsJson = "",
        CancellationToken cancellationToken = default)
    {
        Session? session;
        lock (gate) sessions.TryGetValue(vm.Id, out session);
        if (session is null || !session.IsActive)
            return new QmpResult(false, T("session.notRunning", "虚拟机没有运行"), "NotRunning");

        try
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Lifetime.Token);
            var sessionToken = linkedCancellation.Token;
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, session.QmpPort, sessionToken);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            await ReadJsonLineAsync(reader, sessionToken);

            await WriteQmpAsync(stream, "{\"execute\":\"qmp_capabilities\"}\r\n", sessionToken);
            var caps = await ReadQmpResponseAsync(reader, sessionToken);
            if (!caps.Succeeded) return caps;

            var request = new JsonObject { ["execute"] = command };
            if (!string.IsNullOrWhiteSpace(argumentsJson))
            {
                var arguments = JsonNode.Parse(argumentsJson);
                if (arguments is not JsonObject)
                    return new QmpResult(false, T("qmp.argumentsObject", "QMP 参数必须是 JSON 对象。"), "InvalidArguments");
                request["arguments"] = arguments;
            }

            await WriteQmpAsync(stream, request.ToJsonString() + "\r\n", sessionToken);
            return await ReadQmpResponseAsync(reader, sessionToken);
        }
        catch (OperationCanceledException) when (session.Lifetime.IsCancellationRequested)
        {
            return new QmpResult(false, T("qmp.connectionClosed", "QMP 连接已关闭。"), "ConnectionClosed");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new QmpResult(false, exception.Message, exception.GetType().Name);
        }
    }

    public async Task<GuestAgentResult> ExecuteGuestAgentAsync(
        VmCfg vm,
        string command,
        string argumentsJson = "",
        CancellationToken cancellationToken = default)
    {
        Session? session;
        lock (gate) sessions.TryGetValue(vm.Id, out session);
        if (session is null || !session.IsActive)
            return new GuestAgentResult(false, T("session.notRunning", "虚拟机没有运行"), "NotRunning");

        try
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Lifetime.Token);
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
                    return new GuestAgentResult(false, T("guestAgent.argumentsObject", "Guest Agent 参数必须是 JSON 对象。"), "InvalidArguments");
                request["arguments"] = arguments;
            }

            await WriteQmpAsync(stream, request.ToJsonString() + "\r\n", sessionToken);
            while (true)
            {
                var line = await ReadJsonLineAsync(reader, sessionToken);
                using var document = JsonDocument.Parse(line.TrimStart('\u00ff'));
                var root = document.RootElement;
                if (root.TryGetProperty("return", out var returned))
                    return new GuestAgentResult(true, JsonSerializer.Serialize(returned, new JsonSerializerOptions { WriteIndented = true }));
                if (root.TryGetProperty("error", out var error))
                {
                    var errorClass = error.TryGetProperty("class", out var value) ? value.GetString() ?? "GuestAgentError" : "GuestAgentError";
                    return new GuestAgentResult(false, JsonSerializer.Serialize(error, new JsonSerializerOptions { WriteIndented = true }), errorClass);
                }
            }
        }
        catch (OperationCanceledException) when (session.Lifetime.IsCancellationRequested)
        {
            return new GuestAgentResult(false, T("guestAgent.unavailable", "Guest Agent 未连接或未安装。"), "ConnectionClosed");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new GuestAgentResult(false, T("guestAgent.unavailable", "Guest Agent 未连接或未安装。") + Environment.NewLine + exception.Message, exception.GetType().Name);
        }
    }

    private static async Task WriteQmpAsync(Stream stream, string command, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(command);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<QmpResult> ReadQmpResponseAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await ReadJsonLineAsync(reader, cancellationToken);
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("return", out var returned))
                return new QmpResult(true, JsonSerializer.Serialize(returned, new JsonSerializerOptions { WriteIndented = true }));
            if (root.TryGetProperty("error", out var error))
            {
                var errorClass = error.TryGetProperty("class", out var value) ? value.GetString() ?? "QmpError" : "QmpError";
                return new QmpResult(false, JsonSerializer.Serialize(error, new JsonSerializerOptions { WriteIndented = true }), errorClass);
            }
        }
    }

    private static async Task<string> ReadJsonLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) throw new EndOfStreamException(LocaleSvc.Current.Get("qmp.connectionClosed", "QMP 连接已关闭。"));
            if (!string.IsNullOrWhiteSpace(line)) return line;
        }
    }

}




