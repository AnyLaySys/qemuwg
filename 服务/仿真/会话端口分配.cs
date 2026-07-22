using System.Net;
using System.Net.Sockets;

namespace QemuWG.服务;

public sealed partial class QEMU会话
{
    private static int 查找空闲端口()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static int 查找空闲VNC端口()
    {
        for (var port = 5900; port <= 5999; port++)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return port;
            }
            catch (SocketException)
            {
            }
        }
        throw new IOException(T("session.displayPortUnavailable", "没有可用的本机显示端口。"));
    }
}
