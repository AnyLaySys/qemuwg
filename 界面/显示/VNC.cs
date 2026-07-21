using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace QemuWG.界面;

internal sealed record 显示帧(int Width, int Height, byte[] Pixels);

internal sealed class VNC显示 : IDisposable
{
    private const int RawEncoding = 0;
    private const int DesktopSizeEncoding = -223;

    private readonly object gate = new();
    private CancellationTokenSource? lifetime;
    private TcpClient? client;
    private Task? receiveTask;
    private byte[] framebuffer = [];
    private int width;
    private int height;
    private long lastFrameTicks;

    public event EventHandler<显示帧>? FrameReady;
    public event EventHandler<string>? ConnectionClosed;

    public bool IsConnected
    {
        get
        {
            lock (gate) return client?.Connected == true && lifetime?.IsCancellationRequested == false;
        }
    }

    public async Task<bool> ConnectAsync(int port, CancellationToken cancellationToken)
    {
        Disconnect();
        var linkedLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (gate) lifetime = linkedLifetime;

        Exception? lastError = null;
        for (var attempt = 0; attempt < 150; attempt++)
        {
            linkedLifetime.Token.ThrowIfCancellationRequested();
            var candidate = new TcpClient { NoDelay = true };
            try
            {
                await candidate.ConnectAsync(IPAddress.Loopback, port, linkedLifetime.Token);
                await InitializeAsync(candidate.GetStream(), linkedLifetime.Token);
                lock (gate) client = candidate;
                receiveTask = ReceiveLoopAsync(candidate, linkedLifetime.Token);
                return true;
            }
            catch (Exception exception) when (exception is SocketException or IOException)
            {
                lastError = exception;
                candidate.Dispose();
                await Task.Delay(200, linkedLifetime.Token);
            }
        }

        应用日志.Write($"VNC display connection failed on port {port}: {lastError?.Message}");
        Disconnect();
        return false;
    }

    public void Disconnect()
    {
        CancellationTokenSource? cancellation;
        TcpClient? currentClient;
        lock (gate)
        {
            cancellation = lifetime;
            currentClient = client;
            lifetime = null;
            client = null;
        }
        try { cancellation?.Cancel(); } catch { }
        currentClient?.Dispose();
        cancellation?.Dispose();
        framebuffer = [];
        width = 0;
        height = 0;
    }

    public void Dispose() => Disconnect();

    private async Task InitializeAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var serverVersion = Encoding.ASCII.GetString(await ReadExactAsync(stream, 12, cancellationToken));
        if (!serverVersion.StartsWith("RFB 003.", StringComparison.Ordinal))
            throw new IOException("The display server returned an invalid RFB version.");

        await stream.WriteAsync(Encoding.ASCII.GetBytes("RFB 003.008\n"), cancellationToken);
        var securityTypeCount = (await ReadExactAsync(stream, 1, cancellationToken))[0];
        if (securityTypeCount == 0)
        {
            var reasonLength = await ReadUInt32Async(stream, cancellationToken);
            var reason = Encoding.UTF8.GetString(await ReadExactAsync(stream, checked((int)reasonLength), cancellationToken));
            throw new IOException(reason);
        }

        var securityTypes = await ReadExactAsync(stream, securityTypeCount, cancellationToken);
        if (!securityTypes.Contains((byte)1))
            throw new IOException("The display server requires an unsupported authentication method.");
        await stream.WriteAsync(new byte[] { 1 }, cancellationToken);

        var securityResult = await ReadUInt32Async(stream, cancellationToken);
        if (securityResult != 0)
        {
            var reasonLength = await ReadUInt32Async(stream, cancellationToken);
            var reason = Encoding.UTF8.GetString(await ReadExactAsync(stream, checked((int)reasonLength), cancellationToken));
            throw new IOException(reason);
        }

        await stream.WriteAsync(new byte[] { 1 }, cancellationToken);
        width = await ReadUInt16Async(stream, cancellationToken);
        height = await ReadUInt16Async(stream, cancellationToken);
        await ReadExactAsync(stream, 16, cancellationToken);
        var nameLength = await ReadUInt32Async(stream, cancellationToken);
        await ReadExactAsync(stream, checked((int)nameLength), cancellationToken);
        ResizeFramebuffer(width, height);

        await SendPixelFormatAsync(stream, cancellationToken);
        await SendEncodingsAsync(stream, cancellationToken);
        await RequestFramebufferUpdateAsync(stream, false, cancellationToken);
    }

    private async Task ReceiveLoopAsync(TcpClient connectedClient, CancellationToken cancellationToken)
    {
        try
        {
            var stream = connectedClient.GetStream();
            while (!cancellationToken.IsCancellationRequested)
            {
                var messageType = (await ReadExactAsync(stream, 1, cancellationToken))[0];
                switch (messageType)
                {
                    case 0:
                        await ReadFramebufferUpdateAsync(stream, cancellationToken);
                        await RequestFramebufferUpdateAsync(stream, true, cancellationToken);
                        break;
                    case 2:
                        break;
                    case 3:
                        await ReadExactAsync(stream, 3, cancellationToken);
                        var textLength = await ReadUInt32Async(stream, cancellationToken);
                        await ReadExactAsync(stream, checked((int)textLength), cancellationToken);
                        break;
                    default:
                        throw new IOException($"Unsupported RFB server message type: {messageType}.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            应用日志.Write("VNC display connection closed: " + exception);
            ConnectionClosed?.Invoke(this, exception.Message);
        }
    }

    private async Task ReadFramebufferUpdateAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        await ReadExactAsync(stream, 1, cancellationToken);
        var rectangleCount = await ReadUInt16Async(stream, cancellationToken);
        var changed = false;
        for (var index = 0; index < rectangleCount; index++)
        {
            var x = await ReadUInt16Async(stream, cancellationToken);
            var y = await ReadUInt16Async(stream, cancellationToken);
            var rectangleWidth = await ReadUInt16Async(stream, cancellationToken);
            var rectangleHeight = await ReadUInt16Async(stream, cancellationToken);
            var encoding = await ReadInt32Async(stream, cancellationToken);
            if (encoding == DesktopSizeEncoding)
            {
                width = rectangleWidth;
                height = rectangleHeight;
                ResizeFramebuffer(width, height);
                changed = true;
                continue;
            }
            if (encoding != RawEncoding)
                throw new IOException($"Unsupported RFB encoding: {encoding}.");

            var pixels = await ReadExactAsync(stream, checked(rectangleWidth * rectangleHeight * 4), cancellationToken);
            CopyRectangle(pixels, x, y, rectangleWidth, rectangleHeight);
            changed = true;
        }

        if (!changed || width <= 0 || height <= 0) return;
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref lastFrameTicks) < 50) return;
        Interlocked.Exchange(ref lastFrameTicks, now);
        FrameReady?.Invoke(this, new 显示帧(width, height, (byte[])framebuffer.Clone()));
    }

    private void ResizeFramebuffer(int newWidth, int newHeight)
    {
        if (newWidth <= 0 || newHeight <= 0) throw new IOException("The display server returned an invalid framebuffer size.");
        framebuffer = new byte[checked(newWidth * newHeight * 4)];
        for (var index = 3; index < framebuffer.Length; index += 4) framebuffer[index] = 255;
    }

    private void CopyRectangle(byte[] source, int x, int y, int rectangleWidth, int rectangleHeight)
    {
        if (x + rectangleWidth > width || y + rectangleHeight > height)
            throw new IOException("The display server returned a rectangle outside the framebuffer.");
        var sourceOffset = 0;
        for (var row = 0; row < rectangleHeight; row++)
        {
            var destinationOffset = ((y + row) * width + x) * 4;
            Buffer.BlockCopy(source, sourceOffset, framebuffer, destinationOffset, rectangleWidth * 4);
            for (var column = 0; column < rectangleWidth; column++)
                framebuffer[destinationOffset + column * 4 + 3] = 255;
            sourceOffset += rectangleWidth * 4;
        }
    }

    private static async Task SendPixelFormatAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var message = new byte[20];
        message[0] = 0;
        message[4] = 32;
        message[5] = 24;
        message[6] = 0;
        message[7] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(8, 2), 255);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(10, 2), 255);
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(12, 2), 255);
        message[14] = 16;
        message[15] = 8;
        message[16] = 0;
        await stream.WriteAsync(message, cancellationToken);
    }

    private static async Task SendEncodingsAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var message = new byte[12];
        message[0] = 2;
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(2, 2), 2);
        BinaryPrimitives.WriteInt32BigEndian(message.AsSpan(4, 4), RawEncoding);
        BinaryPrimitives.WriteInt32BigEndian(message.AsSpan(8, 4), DesktopSizeEncoding);
        await stream.WriteAsync(message, cancellationToken);
    }

    private async Task RequestFramebufferUpdateAsync(NetworkStream stream, bool incremental, CancellationToken cancellationToken)
    {
        var message = new byte[10];
        message[0] = 3;
        message[1] = incremental ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(6, 2), checked((ushort)width));
        BinaryPrimitives.WriteUInt16BigEndian(message.AsSpan(8, 2), checked((ushort)height));
        await stream.WriteAsync(message, cancellationToken);
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        var result = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(result.AsMemory(offset, count - offset), cancellationToken);
            if (read == 0) throw new EndOfStreamException("The display connection was closed.");
            offset += read;
        }
        return result;
    }

    private static async Task<ushort> ReadUInt16Async(Stream stream, CancellationToken cancellationToken) =>
        BinaryPrimitives.ReadUInt16BigEndian(await ReadExactAsync(stream, 2, cancellationToken));

    private static async Task<uint> ReadUInt32Async(Stream stream, CancellationToken cancellationToken) =>
        BinaryPrimitives.ReadUInt32BigEndian(await ReadExactAsync(stream, 4, cancellationToken));

    private static async Task<int> ReadInt32Async(Stream stream, CancellationToken cancellationToken) =>
        BinaryPrimitives.ReadInt32BigEndian(await ReadExactAsync(stream, 4, cancellationToken));
}
