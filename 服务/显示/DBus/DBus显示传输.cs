using System.Net.Sockets;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Channels;
using QemuWG.数据;
using Tmds.DBus.Protocol;

namespace QemuWG.服务;

/// <summary>
/// 建立 QEMU D-Bus 显示的两级点对点连接，并把显示回调转交给渲染层。
/// 本类型不决定 QEMU 的原生显示后端；GTK、SDL 等可与 D-Bus 同时启动。
/// </summary>
public sealed class DBus显示传输 : IAsyncDisposable
{
    public const string QEMU显示参数 = "dbus,p2p=on";
    private static readonly TimeSpan 连接超时 = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan 输入超时 = TimeSpan.FromSeconds(1);

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
    /// QEMU 必须已使用 -display dbus,p2p=on 启动；显式使用 GL 显卡时再追加 gl=on。
    /// </summary>
    public async Task 连接(
        int qemu进程标识,
        Func<IReadOnlyList<QMP请求>, CancellationToken, Task<IReadOnlyList<QMP结果>>> 执行QMP批次,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(执行QMP批次);
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
            var requests = new QMP请求[]
            {
                new("get-win32-socket", JsonSerializer.Serialize(new { info, fdname })),
                new("add_client", JsonSerializer.Serialize(new { protocol = "@dbus-display", fdname }))
            };
            var results = await 执行QMP批次(requests, cancellationToken);
            确保QMP批次成功(requests, results);

            connection = 创建客户端连接(套接字对.本地套接字!);
            套接字对 = 套接字对 with { 本地套接字 = null };
            await connection.ConnectAsync().AsTask().WaitAsync(连接超时, cancellationToken);

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
            listenerConnection = 创建客户端连接(套接字对.本地套接字!, handler);
            套接字对 = 套接字对 with { 本地套接字 = null };
            var 连接任务 = listenerConnection.ConnectAsync().AsTask();
            var 注册任务 = 调用注册监听器(
                main,
                控制台标识,
                套接字对.远程套接字信息,
                cancellationToken);
            await Task.WhenAll(连接任务, 注册任务).WaitAsync(连接超时, cancellationToken);

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

    public Task 按下按键(uint 按键码, int 控制台标识 = 0, CancellationToken cancellationToken = default) =>
        调用输入方法(控制台标识, "org.qemu.Display1.Keyboard", "Press", 按键码, null, cancellationToken);

    public Task 释放按键(uint 按键码, int 控制台标识 = 0, CancellationToken cancellationToken = default) =>
        调用输入方法(控制台标识, "org.qemu.Display1.Keyboard", "Release", 按键码, null, cancellationToken);

    public Task 设置鼠标位置(uint 横坐标, uint 纵坐标, int 控制台标识 = 0, CancellationToken cancellationToken = default) =>
        调用输入方法(控制台标识, "org.qemu.Display1.Mouse", "SetAbsPosition", 横坐标, 纵坐标, cancellationToken);

    public Task 移动鼠标相对(int 横向位移, int 纵向位移, int 控制台标识 = 0, CancellationToken cancellationToken = default) =>
        调用鼠标相对移动(控制台标识, 横向位移, 纵向位移, cancellationToken);

    public Task<bool> 获取鼠标是否绝对定位(int 控制台标识 = 0, CancellationToken cancellationToken = default) =>
        调用鼠标模式属性(控制台标识, cancellationToken);

    public Task 按下鼠标按钮(uint 按钮, int 控制台标识 = 0, CancellationToken cancellationToken = default) =>
        调用输入方法(控制台标识, "org.qemu.Display1.Mouse", "Press", 按钮, null, cancellationToken);

    public Task 释放鼠标按钮(uint 按钮, int 控制台标识 = 0, CancellationToken cancellationToken = default) =>
        调用输入方法(控制台标识, "org.qemu.Display1.Mouse", "Release", 按钮, null, cancellationToken);

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
        显示监听处理器? handler;
        lock (同步锁)
        {
            if (已释放) return;
            已释放 = true;
            main = 主连接;
            listener = 监听连接;
            handler = 监听处理器;
            主连接 = null;
            监听连接 = null;
            监听处理器 = null;
        }

        收到D3D11纹理 = null;
        更新D3D11纹理 = null;
        收到共享映射 = null;
        更新共享映射 = null;
        收到位图 = null;
        更新位图 = null;
        停用显示 = null;
        设置指针位置 = null;
        定义指针图像 = null;
        handler?.停止接收();
        listener?.Dispose();
        if (handler is not null) await handler.DisposeAsync();
        main?.Dispose();
    }

    private static void 确保QMP批次成功(
        IReadOnlyList<QMP请求> requests,
        IReadOnlyList<QMP结果> results)
    {
        for (var index = 0; index < requests.Count; index++)
        {
            if (index >= results.Count)
                throw new QMP显示连接异常(requests[index].命令, "MissingResponse", "QMP 没有返回响应。");
            var result = results[index];
            if (!result.Succeeded)
                throw new QMP显示连接异常(requests[index].命令, result.ErrorClass, result.Output);
        }
    }

    private static DBusConnection 创建客户端连接(Socket socket, IPathMethodHandler? 预注册处理器 = null)
    {
        var stream = new NetworkStream(socket, ownsSocket: true);
        if (预注册处理器 is null)
            return new DBusConnection(new 已连接流选项(stream));

        DBusConnection? connection = null;
        var options = new 已连接流选项(stream, () => 预注册方法处理器(connection!, 预注册处理器));
        connection = new DBusConnection(options);
        return connection;
    }

    /// <summary>
    /// Tmds.DBus.Protocol 0.94.2 只允许在 ConnectAsync 完成后调用 AddMethodHandler，
    /// 但 QEMU 会在 D-Bus 认证的 BEGIN 后立即发送 Listener 方法，形成无法关闭的竞态。
    /// 在 BEGIN 写入对端之前，把处理器放入当前版本库的内部路径表，确保首个方法已有接收者。
    /// </summary>
    private static void 预注册方法处理器(DBusConnection connection, IPathMethodHandler handler)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var inner = typeof(DBusConnection).GetField("_connection", flags)?.GetValue(connection)
                    ?? throw new InvalidOperationException("D-Bus 内部连接尚未创建，无法预注册显示处理器。");
        var pathNodes = inner.GetType().GetField("_pathNodes", flags)?.GetValue(inner)
                        ?? throw new MissingFieldException(inner.GetType().FullName, "_pathNodes");
        var addMethod = pathNodes.GetType().GetMethod(
                            "AddMethodHandler",
                            flags,
                            binder: null,
                            types: [typeof(IPathMethodHandler)],
                            modifiers: null)
                        ?? throw new MissingMethodException(pathNodes.GetType().FullName, "AddMethodHandler");
        try
        {
            addMethod.Invoke(pathNodes, [handler]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    private static async Task 调用注册监听器(
        DBusConnection connection,
        int 控制台标识,
        byte[] 远程套接字信息,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var message = 创建注册监听器消息(connection, 控制台标识, 远程套接字信息);
        await connection.CallMethodAsync(message).WaitAsync(连接超时, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task 调用输入方法(
        int 控制台标识,
        string 接口,
        string 成员,
        uint 第一个参数,
        uint? 第二个参数,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DBusConnection connection;
        lock (同步锁) connection = 主连接 ?? throw new InvalidOperationException("D-Bus 显示传输尚未连接。");
        var message = 创建输入消息(connection, 控制台标识, 接口, 成员, 第一个参数, 第二个参数);
        await connection.CallMethodAsync(message).WaitAsync(输入超时, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task 调用鼠标相对移动(
        int 控制台标识,
        int 横向位移,
        int 纵向位移,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DBusConnection connection;
        lock (同步锁) connection = 主连接 ?? throw new InvalidOperationException("D-Bus 显示传输尚未连接。");
        var message = 创建鼠标相对移动消息(connection, 控制台标识, 横向位移, 纵向位移);
        await connection.CallMethodAsync(message).WaitAsync(输入超时, cancellationToken);
    }

    private async Task<bool> 调用鼠标模式属性(int 控制台标识, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DBusConnection connection;
        lock (同步锁) connection = 主连接 ?? throw new InvalidOperationException("D-Bus 显示传输尚未连接。");
        var message = 创建鼠标模式属性消息(connection, 控制台标识);
        return await connection.CallMethodAsync(
            message,
            static (reply, _) => reply.GetBodyReader().ReadVariantValue().GetBool())
            .WaitAsync(输入超时, cancellationToken);
    }

    private static MessageBuffer 创建鼠标相对移动消息(
        DBusConnection connection,
        int 控制台标识,
        int 横向位移,
        int 纵向位移)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            path: $"/org/qemu/Display1/Console_{控制台标识}",
            @interface: "org.qemu.Display1.Mouse",
            member: "RelMotion",
            signature: "ii");
        writer.WriteInt32(横向位移);
        writer.WriteInt32(纵向位移);
        return writer.CreateMessage();
    }

    private static MessageBuffer 创建鼠标模式属性消息(DBusConnection connection, int 控制台标识)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            path: $"/org/qemu/Display1/Console_{控制台标识}",
            @interface: "org.freedesktop.DBus.Properties",
            member: "Get",
            signature: "ss");
        writer.WriteString("org.qemu.Display1.Mouse");
        writer.WriteString("IsAbsolute");
        return writer.CreateMessage();
    }

    private static MessageBuffer 创建输入消息(
        DBusConnection connection,
        int 控制台标识,
        string 接口,
        string 成员,
        uint 第一个参数,
        uint? 第二个参数)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            path: $"/org/qemu/Display1/Console_{控制台标识}",
            @interface: 接口,
            member: 成员,
            signature: 第二个参数.HasValue ? "uu" : "u");
        writer.WriteUInt32(第一个参数);
        if (第二个参数.HasValue) writer.WriteUInt32(第二个参数.Value);
        return writer.CreateMessage();
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

    private sealed class 已连接流选项(Stream stream, Action? 认证完成前操作 = null) : DBusConnectionOptions
    {
        protected override ValueTask<SetupResult> SetupAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new SetupResult
            {
                ConnectionStream = 认证完成前操作 is null ? stream : new 认证门控流(stream, 认证完成前操作),
                UserId = WindowsIdentity.GetCurrent().User?.Value
            });
        }

        protected override void Teardown(object? token)
        {
            stream.Dispose();
        }
    }

    private sealed class 认证门控流(Stream stream, Action 认证完成前操作) : Stream
    {
        private Action? 待执行操作 = 认证完成前操作;

        public override bool CanRead => stream.CanRead;
        public override bool CanSeek => stream.CanSeek;
        public override bool CanWrite => stream.CanWrite;
        public override long Length => stream.Length;
        public override long Position { get => stream.Position; set => stream.Position = value; }
        public override void Flush() => stream.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => stream.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            stream.ReadAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            stream.ReadAsync(buffer, offset, count, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => stream.Seek(offset, origin);
        public override void SetLength(long value) => stream.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            尝试预注册(buffer.AsSpan(offset, count));
            stream.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            尝试预注册(buffer);
            stream.Write(buffer);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            尝试预注册(buffer.AsSpan(offset, count));
            return stream.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            尝试预注册(buffer.Span);
            return stream.WriteAsync(buffer, cancellationToken);
        }

        private void 尝试预注册(ReadOnlySpan<byte> buffer)
        {
            if (!buffer.SequenceEqual("BEGIN\r\n"u8)) return;
            Interlocked.Exchange(ref 待执行操作, null)?.Invoke();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) stream.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await stream.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }

    private sealed class 显示监听处理器 : IPathMethodHandler, IAsyncDisposable
    {
        private static readonly string[] 接口 =
        [
            "org.qemu.Display1.Listener.Win32.D3d11",
            "org.qemu.Display1.Listener.Win32.Map"
        ];

        private readonly DBus显示传输 owner;
        private readonly Channel<MethodContext> 队列;
        private readonly Task 队列任务;

        public 显示监听处理器(DBus显示传输 owner)
        {
            this.owner = owner;
            队列 = Channel.CreateUnbounded<MethodContext>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
            队列任务 = Task.Run(处理队列);
        }

        public string Path => "/org/qemu/Display1/Listener";
        public bool HandlesChildPaths => false;

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            // Properties.GetAll 属于 Listener 建立握手，必须立即回复；它不触碰 GPU 或界面。
            if (context.Request.InterfaceAsString == "org.freedesktop.DBus.Properties")
            {
                try
                {
                    处理属性(context);
                }
                catch (Exception exception)
                {
                    try
                    {
                        if (!context.ReplySent)
                            context.ReplyError("org.qemu.Display1.Listener.Error", exception.Message);
                    }
                    catch { }
                }
                return default;
            }

            context.DisposesAsynchronously = true;
            // 显示调用交给单读者队列：Tmds 接收循环立即返回，不会被 GPU 或界面调度阻塞。
            // MethodContext 由队列持有，只有渲染完成（包括 KeyedMutex ReleaseSync）后才回复并释放，
            // 因而不会为了异步化而破坏 QEMU 的纹理互斥时序。
            if (!队列.Writer.TryWrite(context))
            {
                try
                {
                    context.ReplyError("org.qemu.Display1.Listener.Error", "显示监听器已停止接收请求。");
                }
                catch { }
                finally
                {
                    context.Dispose();
                }
            }
            return default;
        }

        public void 停止接收() => 队列.Writer.TryComplete();

        public async ValueTask DisposeAsync()
        {
            停止接收();
            try { await 队列任务; }
            catch { }
        }

        private async Task 处理队列()
        {
            await foreach (var context in 队列.Reader.ReadAllAsync())
                await 处理(context);
        }

        private async Task 处理(MethodContext context)
        {
            try
            {
                var request = context.Request;
                switch (request.InterfaceAsString)
                {
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
                try
                {
                    if (!context.ReplySent)
                        context.ReplyError("org.qemu.Display1.Listener.Error", exception.Message);
                }
                catch
                {
                    // 对端可能已在回调执行期间断开；连接关闭即是完整的错误传播。
                }
            }
            finally
            {
                try { context.Dispose(); }
                catch { }
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
