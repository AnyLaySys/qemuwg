using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using QemuWG.服务;
using SharpGen.Runtime;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace QemuWG.界面.显示;

/// <summary>
/// 把 QEMU D-Bus 显示提供的 D3D11 共享纹理或 Win32 共享映射呈现到 WinUI。
/// 所有公开的“接收/更新”方法都是同步完成 GPU 复制后才返回，以满足 QEMU KeyedMutex 0 的时序要求。
/// </summary>
public sealed class D3D11内嵌显示 : IDisposable
{
    private const int 等待共享纹理毫秒数 = 5000;
    private const uint 文件映射读取 = 0x0004;
    private const uint PixmanX8R8G8B8 = 0x20020888;
    private const uint PixmanA8R8G8B8 = 0x20028888;
    private const uint PixmanX8B8G8R8 = 0x20030888;
    private const uint PixmanA8B8G8R8 = 0x20038888;

    private static readonly FeatureLevel[] 功能级别 =
    [
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0
    ];

    private readonly object 同步锁 = new();
    private readonly SwapChainPanel 显示面板;
    private readonly DispatcherQueue 界面队列;

    private IDXGIFactory2? 工厂;
    private ID3D11Device? 设备;
    private ID3D11Device1? 设备一;
    private ID3D11DeviceContext? 设备上下文;
    private Luid? 设备适配器标识;
    private IDXGISwapChain1? 交换链;
    private ID3D11Texture2D? 后台缓冲;
    private uint 显示宽度;
    private uint 显示高度;
    private Format 显示格式;

    private ID3D11Texture2D? QEMU共享纹理;
    private IDXGIKeyedMutex? QEMU共享互斥锁;
    private uint 纹理横向偏移;
    private uint 纹理纵向偏移;
    private bool 纹理原点在顶部 = true;

    private nint 共享映射地址;
    private nuint 共享映射长度;
    private nuint 共享映射数据偏移;
    private uint 共享映射跨距;
    private uint 共享映射宽度;
    private uint 共享映射高度;
    private bool 已释放;

    public D3D11内嵌显示(SwapChainPanel 显示面板)
    {
        this.显示面板 = 显示面板 ?? throw new ArgumentNullException(nameof(显示面板));
        界面队列 = 显示面板.DispatcherQueue;
    }

    public bool 已准备
    {
        get
        {
            lock (同步锁) return !已释放 && 交换链 is not null;
        }
    }

    public void 接收共享纹理(D3D11纹理扫描 扫描)
    {
        lock (同步锁)
        {
            检查未释放();
            释放共享纹理();
            释放共享映射();

            var 句柄 = 扫描.共享句柄;
            if (句柄 == 0) throw new ArgumentException("QEMU 提供了无效的 D3D11 共享纹理句柄。", nameof(扫描));

            try
            {
                准备共享纹理设备(句柄);
                QEMU共享纹理 = 设备一!.OpenSharedResource1<ID3D11Texture2D>(句柄);
                QEMU共享互斥锁 = QEMU共享纹理.QueryInterface<IDXGIKeyedMutex>();
            }
            finally
            {
                CloseHandle(句柄);
            }

            var 纹理说明 = QEMU共享纹理.Description;
            var 宽度 = 扫描.显示宽度 == 0 ? 扫描.纹理宽度 : 扫描.显示宽度;
            var 高度 = 扫描.显示高度 == 0 ? 扫描.纹理高度 : 扫描.显示高度;
            if (宽度 == 0 || 高度 == 0) throw new InvalidOperationException("QEMU 共享纹理的显示尺寸无效。");

            纹理横向偏移 = 扫描.横向偏移;
            纹理纵向偏移 = 扫描.纵向偏移;
            纹理原点在顶部 = 扫描.原点在顶部;
            if (!纹理原点在顶部)
                throw new NotSupportedException("当前 D3D11 快速复制路径要求 QEMU 纹理原点位于顶部。");

            var 格式 = 规范交换链格式(纹理说明.Format);
            准备交换链(宽度, 高度, 格式);
            复制共享纹理并呈现();
        }
    }

    public void 更新共享纹理(显示更新区域 _)
    {
        lock (同步锁)
        {
            检查未释放();
            if (QEMU共享纹理 is null || QEMU共享互斥锁 is null) return;
            复制共享纹理并呈现();
        }
    }

    public void 接收共享映射(共享映射扫描 扫描)
    {
        lock (同步锁)
        {
            检查未释放();
            释放共享纹理();
            释放共享映射();

            if (扫描.共享句柄 == 0) throw new ArgumentException("QEMU 提供了无效的共享映射句柄。", nameof(扫描));
            if (扫描.宽度 == 0 || 扫描.高度 == 0 || 扫描.跨距 == 0)
                throw new ArgumentException("QEMU 共享映射的尺寸无效。", nameof(扫描));

            GetSystemInfo(out var 系统信息);
            var 粒度 = (ulong)系统信息.分配粒度;
            var 映射起点 = (ulong)扫描.映射偏移 / 粒度 * 粒度;
            var 数据偏移 = (ulong)扫描.映射偏移 - 映射起点;
            var 数据长度 = checked((ulong)扫描.跨距 * 扫描.高度);
            var 映射长度 = checked(数据偏移 + 数据长度);

            try
            {
                共享映射地址 = MapViewOfFile(
                    扫描.共享句柄,
                    文件映射读取,
                    (uint)(映射起点 >> 32),
                    (uint)映射起点,
                    checked((nuint)映射长度));
            }
            finally
            {
                CloseHandle(扫描.共享句柄);
            }

            if (共享映射地址 == 0)
                throw new InvalidOperationException($"无法映射 QEMU 显示内存，Win32 错误 {Marshal.GetLastWin32Error()}。");

            共享映射长度 = checked((nuint)映射长度);
            共享映射数据偏移 = checked((nuint)数据偏移);
            共享映射跨距 = 扫描.跨距;
            共享映射宽度 = 扫描.宽度;
            共享映射高度 = 扫描.高度;

            准备默认设备();
            准备交换链(扫描.宽度, 扫描.高度, 转换Pixman格式(扫描.像素格式));
            上传共享映射并呈现();
        }
    }

    public void 更新共享映射(显示更新区域 _)
    {
        lock (同步锁)
        {
            检查未释放();
            if (共享映射地址 == 0) return;
            上传共享映射并呈现();
        }
    }

    public void 接收位图(位图扫描 扫描)
    {
        ArgumentNullException.ThrowIfNull(扫描.数据);
        lock (同步锁)
        {
            检查未释放();
            if (扫描.宽度 == 0 || 扫描.高度 == 0 || 扫描.跨距 == 0) return;
            var 必需字节数 = checked((ulong)扫描.跨距 * 扫描.高度);
            if ((ulong)扫描.数据.LongLength < 必需字节数)
                throw new ArgumentException("位图数据小于跨距与高度要求的大小。", nameof(扫描));

            释放共享纹理();
            释放共享映射();
            准备默认设备();
            准备交换链(扫描.宽度, 扫描.高度, 转换Pixman格式(扫描.像素格式));
            设备上下文!.UpdateSubresource<byte>(扫描.数据, 后台缓冲!, 0, 扫描.跨距, 0, null);
            交换链!.Present(0, PresentFlags.None).CheckError();
        }
    }

    public void 更新位图(位图更新 更新)
    {
        ArgumentNullException.ThrowIfNull(更新.数据);
        lock (同步锁)
        {
            检查未释放();
            if (后台缓冲 is null || 更新.宽度 <= 0 || 更新.高度 <= 0) return;
            if (更新.横向偏移 < 0 || 更新.纵向偏移 < 0 ||
                (uint)(更新.横向偏移 + 更新.宽度) > 显示宽度 ||
                (uint)(更新.纵向偏移 + 更新.高度) > 显示高度)
                throw new ArgumentOutOfRangeException(nameof(更新), "位图更新区域超出了当前显示范围。");

            var 必需字节数 = checked((ulong)更新.跨距 * (uint)更新.高度);
            if ((ulong)更新.数据.LongLength < 必需字节数)
                throw new ArgumentException("位图更新数据小于跨距与高度要求的大小。", nameof(更新));

            var 区域 = new Box(
                更新.横向偏移,
                更新.纵向偏移,
                0,
                更新.横向偏移 + 更新.宽度,
                更新.纵向偏移 + 更新.高度,
                1);
            设备上下文!.UpdateSubresource<byte>(更新.数据, 后台缓冲, 0, 更新.跨距, 0, 区域);
            交换链!.Present(0, PresentFlags.None).CheckError();
        }
    }

    public void 停用显示()
    {
        lock (同步锁)
        {
            if (已释放) return;
            释放共享纹理();
            释放共享映射();
        }
    }

    public void Dispose()
    {
        lock (同步锁)
        {
            if (已释放) return;
            已释放 = true;
            释放共享纹理();
            释放共享映射();
            在界面线程执行(() =>
            {
                using var 面板接口 = new ISwapChainPanelNative(WinRT.MarshalInspectable<SwapChainPanel>.FromManaged(显示面板));
                面板接口.SetSwapChain(null).CheckError();
            });
            释放交换链();
            设备上下文?.Dispose();
            设备上下文 = null;
            设备一?.Dispose();
            设备一 = null;
            设备?.Dispose();
            设备 = null;
            工厂?.Dispose();
            工厂 = null;
        }
        GC.SuppressFinalize(this);
    }

    private void 复制共享纹理并呈现()
    {
        if (!纹理原点在顶部) return;
        var 互斥锁 = QEMU共享互斥锁!;
        var 已取得 = false;
        try
        {
            互斥锁.AcquireSync(0, 等待共享纹理毫秒数);
            已取得 = true;
            var 区域 = new Box(
                checked((int)纹理横向偏移),
                checked((int)纹理纵向偏移),
                0,
                checked((int)(纹理横向偏移 + 显示宽度)),
                checked((int)(纹理纵向偏移 + 显示高度)),
                1);
            设备上下文!.CopySubresourceRegion(后台缓冲!, 0, 0, 0, 0, QEMU共享纹理!, 0, 区域);
        }
        finally
        {
            if (已取得) 互斥锁.ReleaseSync(0);
        }
        交换链!.Present(0, PresentFlags.None).CheckError();
    }

    private void 上传共享映射并呈现()
    {
        var 数据地址 = 共享映射地址 + checked((nint)共享映射数据偏移);
        设备上下文!.UpdateSubresource(
            后台缓冲!,
            0,
            null,
            数据地址,
            共享映射跨距,
            0);
        交换链!.Present(0, PresentFlags.None).CheckError();
    }

    private void 准备共享纹理设备(nint 共享句柄)
    {
        工厂 ??= CreateDXGIFactory2<IDXGIFactory2>(false);
        var 标识 = 工厂.GetSharedResourceAdapterLuid(共享句柄);
        if (设备 is not null && 设备适配器标识 == 标识) return;

        using var 工厂四 = 工厂.QueryInterface<IDXGIFactory4>();
        using var 适配器 = 工厂四.EnumAdapterByLuid<IDXGIAdapter1>(标识);
        重建设备(适配器, DriverType.Unknown, 标识);
    }

    private void 准备默认设备()
    {
        if (设备 is not null) return;
        重建设备(null, DriverType.Hardware, null);
    }

    private void 重建设备(IDXGIAdapter1? 适配器, DriverType 驱动类型, Luid? 标识)
    {
        释放交换链();
        设备上下文?.Dispose();
        设备一?.Dispose();
        设备?.Dispose();
        工厂?.Dispose();

        工厂 = CreateDXGIFactory2<IDXGIFactory2>(false);
        ID3D11Device 新设备;
        ID3D11DeviceContext 新上下文;
        D3D11CreateDevice(
            适配器,
            驱动类型,
            DeviceCreationFlags.BgraSupport,
            功能级别,
            out 新设备,
            out 新上下文).CheckError();
        设备 = 新设备;
        设备一 = 新设备.QueryInterface<ID3D11Device1>();
        设备上下文 = 新上下文;
        设备适配器标识 = 标识;
    }

    private void 准备交换链(uint 宽度, uint 高度, Format 格式)
    {
        if (交换链 is not null && 显示宽度 == 宽度 && 显示高度 == 高度 && 显示格式 == 格式) return;
        释放交换链();

        var 说明 = new SwapChainDescription1(
            宽度,
            高度,
            格式,
            false,
            Usage.RenderTargetOutput,
            2,
            Scaling.Stretch,
            SwapEffect.FlipSequential,
            AlphaMode.Ignore,
            SwapChainFlags.None);
        交换链 = 工厂!.CreateSwapChainForComposition(设备!, 说明, null);
        后台缓冲 = 交换链.GetBuffer<ID3D11Texture2D>(0);
        显示宽度 = 宽度;
        显示高度 = 高度;
        显示格式 = 格式;

        在界面线程执行(() =>
        {
            显示面板.Width = 宽度;
            显示面板.Height = 高度;
            using var 面板接口 = new ISwapChainPanelNative(WinRT.MarshalInspectable<SwapChainPanel>.FromManaged(显示面板));
            面板接口.SetSwapChain(交换链).CheckError();
        });
    }

    private void 释放交换链()
    {
        后台缓冲?.Dispose();
        后台缓冲 = null;
        交换链?.Dispose();
        交换链 = null;
        显示宽度 = 0;
        显示高度 = 0;
        显示格式 = Format.Unknown;
    }

    private void 释放共享纹理()
    {
        QEMU共享互斥锁?.Dispose();
        QEMU共享互斥锁 = null;
        QEMU共享纹理?.Dispose();
        QEMU共享纹理 = null;
    }

    private void 释放共享映射()
    {
        if (共享映射地址 != 0) UnmapViewOfFile(共享映射地址);
        共享映射地址 = 0;
        共享映射长度 = 0;
        共享映射数据偏移 = 0;
        共享映射跨距 = 0;
        共享映射宽度 = 0;
        共享映射高度 = 0;
    }

    private void 在界面线程执行(Action 操作)
    {
        if (界面队列.HasThreadAccess)
        {
            操作();
            return;
        }

        var 完成 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!界面队列.TryEnqueue(() =>
            {
                try
                {
                    操作();
                    完成.SetResult();
                }
                catch (Exception 异常)
                {
                    完成.SetException(异常);
                }
            }))
            throw new InvalidOperationException("无法把 D3D11 交换链附加到 WinUI 显示面板。");
        完成.Task.GetAwaiter().GetResult();
    }

    private void 检查未释放()
    {
        ObjectDisposedException.ThrowIf(已释放, this);
    }

    private static Format 规范交换链格式(Format 格式) => 格式 switch
    {
        Format.B8G8R8A8_Typeless => Format.B8G8R8A8_UNorm,
        Format.R8G8B8A8_Typeless => Format.R8G8B8A8_UNorm,
        Format.B8G8R8A8_UNorm or Format.B8G8R8A8_UNorm_SRgb or
        Format.R8G8B8A8_UNorm or Format.R8G8B8A8_UNorm_SRgb => 格式,
        _ => throw new NotSupportedException($"尚不支持 QEMU D3D11 纹理格式 {格式}。")
    };

    private static Format 转换Pixman格式(uint 格式) => 格式 switch
    {
        0 or PixmanX8R8G8B8 or PixmanA8R8G8B8 => Format.B8G8R8A8_UNorm,
        PixmanX8B8G8R8 or PixmanA8B8G8R8 => Format.R8G8B8A8_UNorm,
        _ => throw new NotSupportedException($"尚不支持 QEMU Pixman 像素格式 0x{格式:X8}。")
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct 系统信息结构
    {
        public ushort 处理器架构;
        public ushort 保留;
        public uint 页面大小;
        public nint 最小应用地址;
        public nint 最大应用地址;
        public nuint 活跃处理器掩码;
        public uint 处理器数量;
        public uint 处理器类型;
        public uint 分配粒度;
        public ushort 处理器级别;
        public ushort 处理器修订;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint 句柄);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint MapViewOfFile(
        nint 文件映射,
        uint 所需访问,
        uint 文件偏移高位,
        uint 文件偏移低位,
        nuint 映射字节数);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnmapViewOfFile(nint 基址);

    [DllImport("kernel32.dll")]
    private static extern void GetSystemInfo(out 系统信息结构 系统信息);
}
