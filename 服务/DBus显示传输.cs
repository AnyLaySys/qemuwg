using System.Net.Sockets;
using System.Security.Principal;
using System.Text.Json;
using QemuWG.数据;
using Tmds.DBus.Protocol;

namespace QemuWG.服务;

public sealed record D3D11纹理扫描(
    nint 共享句柄,
    uint 纹理宽度,
    uint 纹理高度,
    bool 原点在顶部,
    uint 横向偏移,
    uint 纵向偏移,
    uint 显示宽度,
    uint 显示高度);

public sealed record 显示更新区域(int 横向偏移, int 纵向偏移, int 宽度, int 高度);

public sealed record 共享映射扫描(
    nint 共享句柄,
    uint 映射偏移,
    uint 宽度,
    uint 高度,
    uint 跨距,
    uint 像素格式);

public sealed record 位图扫描(uint 宽度, uint 高度, uint 跨距, uint 像素格式, byte[] 数据);

public sealed record 位图更新(
    int 横向偏移,
    int 纵向偏移,
    int 宽度,
    int 高度,
    uint 跨距,
    uint 像素格式,
    byte[] 数据);

public sealed record 指针图像(int 宽度, int 高度, int 热点横坐标, int 热点纵坐标, byte[] 数据);

public sealed record 指针位置(int 横坐标, int 纵坐标, bool 可见);

/// <summary>
/// 建立 QEMU D-Bus 显示的两级点对点连接，并把显示回调转交给渲染层。
/// 本类型不决定 QEMU 的原生显示后端；GTK、SDL 等可与 D-Bus 同时启动。
/// </summary>
public sealed class DBus显示传输 : IAsyncDisposable
{
    public const string QEMU显示参数 = "dbus,p2p=on,gl=on";

    private readonly object 同步锁 = new();
    private DBusConnection? 主连接;
    private DBusConnection? 监听连接;
    private 显示监听处理器? 监听处理器;
    private bool 已释放;

    public Func<D3D11纹理扫描, CancellationToken, ValueTask>? 收到D3D11纹理 { get; set; }
    public Func<显示更新区域, CancellationToken, ValueTask>? 更新D3D11纹理 { get; set; }
    public Func<共享映射扫描, CancellationToken, ValueTask>? 收到共享映射 { get; set; }
    public Func<显示更新区域, CancellationToken, ValueTask>? 更新共享映射 { get; set; }
    public Func<位图扫描, CancellationToken, ValueTask>? 收到位图 { get; set; }
    public Func<位图更新, CancellationToken, ValueTask>? 更新位图 { get; set; }
    public Func<CancellationToken, ValueTask>? 停用显示 { get; set; }
    public Func<指针位置, CancellationToken, ValueTask>? 设置指针位置 { get; set; }
    public Func<指针图像, CancellationToken, ValueTask>? 定义指针图像 { get; set; }

    public bool 已连接
    {
        get
        {
            lock (同步锁) return 主连接 is not null;
        }
    }

    /// <summary>
    /// 把一个 AF_UNIX 套接字复制给 QEMU，并通过 QMP 将其接入 dbus-display。
    /// QEMU 必须已使用 -display dbus,p2p=on,gl=on 启动。
    /// </summary>
    public async Task 连接(
        int qemu进程标识,
        Func<string, string, CancellationToken, Task<QMP结果>> 执行QMP,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(执行QMP);
        ObjectDisposedException.ThrowIf(已释放, this);
        QEMU进程标识 = qemu进程标识;

        lock (同步锁)
        {
            if (主连接 is not null) throw new InvalidOperationException("D-Bus 显示传输已经连接。");
        }

        var 套接字对 = await 创建已复制套接字(qemu进程标识, cancellationToken);
        DBusConnection? connection = null;
        try
        {
            var info = Convert.ToBase64String(套接字对.远程套接字信息);
            var fdname = $"qemuwg-dbus-{Guid.NewGuid():N}";
            await 确保QMP成功(
                执行QMP,
                "get-win32-socket",
                JsonSerializer.Serialize(new { info, fdname }),
                cancellationToken);
            await 确保QMP成功(
                执行QMP,
                "add_client",
                JsonSerializer.Serialize(new { protocol = "dbus-display", fdname }),
                cancellationToken);

            connection = 创建客户端连接(套接字对.本地套接字!);
            套接字对 = 套接字对 with { 本地套接字 = null };
            await connection.ConnectAsync();

            lock (同步锁)
            {
                if (已释放) throw new ObjectDisposedException(nameof(DBus显示传输));
                主连接 = connection;
                connection = null;
            }
        }
        finally
        {
            套接字对.本地套接字?.Dispose();
            connection?.Dispose();
        }
    }

    /// <summary>
    /// 为指定控制台注册 Listener。默认图形控制台路径是 Console_0。
    /// </summary>
    public async Task 注册控制台(int 控制台标识 = 0, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(已释放, this);
        DBusConnection main;
        lock (同步锁)
        {
            main = 主连接 ?? throw new InvalidOperationException("请先连接 D-Bus 显示传输。");
            if (监听连接 is not null) throw new InvalidOperationException("显示监听器已经注册。");
        }

        var qemu进程标识 = 获取QEMU进程标识();
        var 套接字对 = await 创建已复制套接字(qemu进程标识, cancellationToken);
        DBusConnection? listenerConnection = null;
        try
        {
            var handler = new 显示监听处理器(this);
            listenerConnection = 创建客户端连接(套接字对.本地套接字!);
            套接字对 = 套接字对 with { 本地套接字 = null };
            listenerConnection.AddMethodHandler(handler);

            var 连接任务 = listenerConnection.ConnectAsync().AsTask();
            var 注册任务 = 调用注册监听器(
                main,
                控制台标识,
                套接字对.远程套接字信息,
                cancellationToken);
            await Task.WhenAll(连接任务, 注册任务);

            lock (同步锁)
            {
                if (已释放) throw new ObjectDisposedException(nameof(DBus显示传输));
                监听处理器 = handler;
                监听连接 = listenerConnection;
                listenerConnection = null;
            }
        }
        finally
        {
            套接字对.本地套接字?.Dispose();
            listenerConnection?.Dispose();
        }
    }

    /// <summary>
    /// 主连接使用的 QEMU PID。集成到会话后应在连接前设置。
    /// </summary>
    public int QEMU进程标识 { get; set; }

    private int 获取QEMU进程标识()
    {
        if (QEMU进程标识 <= 0)
            throw new InvalidOperationException("注册控制台前必须设置 QEMU进程标识。");
        return QEMU进程标识;
    }

    public async ValueTask DisposeAsync()
    {
        DBusConnection? main;
        DBusConnection? listener;
        lock (同步锁)
        {
            if (已释放) return;
            已释放 = true;
            main = 主连接;
            listener = 监听连接;
            主连接 = null;
            监听连接 = null;
            监听处理器 = null;
        }

        listener?.Dispose();
        main?.Dispose();
        await Task.CompletedTask;
    }

    private static async Task 确保QMP成功(
        Func<string, string, CancellationToken, Task<QMP结果>> 执行QMP,
        string 命令,
        string 参数,
        CancellationToken cancellationToken)
    {
        var result = await 执行QMP(命令, 参数, cancellationToken);
        if (!result.Succeeded)
            throw new InvalidOperationException($"QMP {命令} 失败：{result.Output}");
    }

    private static DBusConnection 创建客户端连接(Socket socket)
    {
        var stream = new NetworkStream(socket, ownsSocket: true);
        return new DBusConnection(new 已连接流选项(stream));
    }

    private static async Task 调用注册监听器(
        DBusConnection connection,
        int 控制台标识,
        byte[] 远程套接字信息,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var message = 创建注册监听器消息(connection, 控制台标识, 远程套接字信息);
        await connection.CallMethodAsync(message);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static MessageBuffer 创建注册监听器消息(
        DBusConnection connection,
        int 控制台标识,
        byte[] 远程套接字信息)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            path: $"/org/qemu/Display1/Console_{控制台标识}",
            @interface: "org.qemu.Display1.Console",
            member: "RegisterListener",
            signature: "ay");
        writer.WriteArray(远程套接字信息);
        return writer.CreateMessage();
    }

    private static async Task<已复制套接字> 创建已复制套接字(
        int qemu进程标识,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Win32 D-Bus 显示传输只支持 Windows。");
        if (qemu进程标识 <= 0) throw new ArgumentOutOfRangeException(nameof(qemu进程标识));

        var path = Path.Combine(Path.GetTempPath(), $"qemuwg-dbus-{Guid.NewGuid():N}.sock");
        Socket? local = null;
        try
        {
            using var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            var endpoint = new UnixDomainSocketEndPoint(path);
            server.Bind(endpoint);
            server.Listen(1);

            using var remote = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            var acceptTask = server.AcceptAsync(cancellationToken).AsTask();
            await remote.ConnectAsync(endpoint, cancellationToken);
            local = await acceptTask;

            var information = remote.DuplicateAndClose(qemu进程标识).ProtocolInformation;
            var result = new 已复制套接字(local, information);
            local = null;
            return result;
        }
        finally
        {
            local?.Dispose();
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed record 已复制套接字(Socket? 本地套接字, byte[] 远程套接字信息);

    private sealed class 已连接流选项(Stream stream) : DBusConnectionOptions
    {
        protected override ValueTask<SetupResult> SetupAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new SetupResult
            {
                ConnectionStream = stream,
                UserId = WindowsIdentity.GetCurrent().User?.Value
            });
        }

        protected override void Teardown(object? token)
        {
            stream.Dispose();
        }
    }

    private sealed class 显示监听处理器(DBus显示传输 owner) : IPathMethodHandler
    {
        private static readonly string[] 接口 =
        [
            "org.qemu.Display1.Listener.Win32.D3d11",
            "org.qemu.Display1.Listener.Win32.Map"
        ];

        public string Path => "/org/qemu/Display1/Listener";
        public bool HandlesChildPaths => false;

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            context.DisposesAsynchronously = true;
            _ = 处理(context);
            return default;
        }

        private async Task 处理(MethodContext context)
        {
            try
            {
                var request = context.Request;
                switch (request.InterfaceAsString)
                {
                    case "org.freedesktop.DBus.Properties":
                        处理属性(context);
                        return;
                    case "org.qemu.Display1.Listener":
                        await 处理基础显示(context);
                        return;
                    case "org.qemu.Display1.Listener.Win32.D3d11":
                        await 处理D3D11(context);
                        return;
                    case "org.qemu.Display1.Listener.Win32.Map":
                        await 处理共享映射(context);
                        return;
                    default:
                        context.ReplyUnknownMethodError();
                        return;
                }
            }
            catch (Exception exception)
            {
                context.ReplyError("org.qemu.Display1.Listener.Error", exception.Message);
            }
            finally
            {
                context.Dispose();
            }
        }

        private static void 处理属性(MethodContext context)
        {
            var request = context.Request;
            var reader = request.GetBodyReader();
            var interfaceName = reader.ReadString();
            switch ((request.MemberAsString, request.SignatureAsString))
            {
                case ("GetAll", "s"):
                {
                    using var writer = context.CreateReplyWriter("a{sv}");
                    var dictionary = writer.WriteDictionaryStart();
                    if (interfaceName == "org.qemu.Display1.Listener")
                    {
                        writer.WriteDictionaryEntryStart();
                        writer.WriteString("Interfaces");
                        writer.WriteVariant(VariantValue.Array(接口));
                    }
                    writer.WriteDictionaryEnd(dictionary);
                    context.Reply(writer.CreateMessage());
                    return;
                }
                case ("Get", "ss"):
                {
                    var propertyName = reader.ReadString();
                    if (interfaceName != "org.qemu.Display1.Listener" || propertyName != "Interfaces")
                    {
                        context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", propertyName);
                        return;
                    }
                    using var writer = context.CreateReplyWriter("v");
                    writer.WriteVariant(VariantValue.Array(接口));
                    context.Reply(writer.CreateMessage());
                    return;
                }
                default:
                    context.ReplyUnknownMethodError();
                    return;
            }
        }

        private async ValueTask 处理基础显示(MethodContext context)
        {
            var request = context.Request;
            switch ((request.MemberAsString, request.SignatureAsString))
            {
                case ("Scanout", "uuuuay"):
                    await 调用(owner.收到位图, 读取位图扫描(request), context.RequestAborted);
                    break;
                case ("Update", "iiiiuuay"):
                    await 调用(owner.更新位图, 读取位图更新(request), context.RequestAborted);
                    break;
                case ("Disable", ""):
                    if (owner.停用显示 is not null) await owner.停用显示(context.RequestAborted);
                    break;
                case ("MouseSet", "iii"):
                    await 调用(owner.设置指针位置, 读取指针位置(request), context.RequestAborted);
                    break;
                case ("CursorDefine", "iiiiay"):
                    await 调用(owner.定义指针图像, 读取指针图像(request), context.RequestAborted);
                    break;
                default:
                    context.ReplyUnknownMethodError();
                    return;
            }
            回复空结果(context);
        }

        private async ValueTask 处理D3D11(MethodContext context)
        {
            var request = context.Request;
            switch ((request.MemberAsString, request.SignatureAsString))
            {
                case ("ScanoutTexture2d", "tuubuuuu"):
                    await 调用(owner.收到D3D11纹理, 读取D3D11纹理扫描(request), context.RequestAborted);
                    break;
                case ("UpdateTexture2d", "iiii"):
                    await 调用(owner.更新D3D11纹理, 读取更新区域(request), context.RequestAborted);
                    break;
                default:
                    context.ReplyUnknownMethodError();
                    return;
            }
            回复空结果(context);
        }

        private async ValueTask 处理共享映射(MethodContext context)
        {
            var request = context.Request;
            switch ((request.MemberAsString, request.SignatureAsString))
            {
                case ("ScanoutMap", "tuuuuu"):
                    await 调用(owner.收到共享映射, 读取共享映射扫描(request), context.RequestAborted);
                    break;
                case ("UpdateMap", "iiii"):
                    await 调用(owner.更新共享映射, 读取更新区域(request), context.RequestAborted);
                    break;
                default:
                    context.ReplyUnknownMethodError();
                    return;
            }
            回复空结果(context);
        }

        private static 位图扫描 读取位图扫描(Message request)
        {
            var reader = request.GetBodyReader();
            return new 位图扫描(
                reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(),
                reader.ReadUInt32(), reader.ReadArrayOfByte());
        }

        private static 位图更新 读取位图更新(Message request)
        {
            var reader = request.GetBodyReader();
            return new 位图更新(
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadArrayOfByte());
        }

        private static 指针位置 读取指针位置(Message request)
        {
            var reader = request.GetBodyReader();
            return new 指针位置(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32() != 0);
        }

        private static 指针图像 读取指针图像(Message request)
        {
            var reader = request.GetBodyReader();
            return new 指针图像(
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadArrayOfByte());
        }

        private static D3D11纹理扫描 读取D3D11纹理扫描(Message request)
        {
            var reader = request.GetBodyReader();
            return new D3D11纹理扫描(
                unchecked((nint)(long)reader.ReadUInt64()),
                reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadBool(),
                reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32());
        }

        private static 共享映射扫描 读取共享映射扫描(Message request)
        {
            var reader = request.GetBodyReader();
            return new 共享映射扫描(
                unchecked((nint)(long)reader.ReadUInt64()),
                reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(),
                reader.ReadUInt32(), reader.ReadUInt32());
        }

        private static 显示更新区域 读取更新区域(Message request)
        {
            var reader = request.GetBodyReader();
            return new 显示更新区域(
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        }

        private static async ValueTask 调用<T>(
            Func<T, CancellationToken, ValueTask>? handler,
            T value,
            CancellationToken cancellationToken)
        {
            if (handler is not null) await handler(value, cancellationToken);
        }

        private static void 回复空结果(MethodContext context)
        {
            using var writer = context.CreateReplyWriter(null);
            context.Reply(writer.CreateMessage());
        }
    }
}
