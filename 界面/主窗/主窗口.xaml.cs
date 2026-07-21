using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QemuWG.数据;
using QemuWG.服务;
using QemuWG.界面;
using WinRT.Interop;

namespace QemuWG;

public sealed partial class 主窗 : Window
{
    private static string T(string key, string fallback) => 语言服务.当前.获取(key, fallback);

    private readonly QEMU服务 qemuSvc = new();
    private readonly 虚拟机仓库 vmRepo = new();
    private readonly QEMU会话 sessions;
    private readonly QEMU工具会话 toolSessions = new();
    private QEMU安装 qemu = new();
    private 虚拟机配置? selectedVm;
    private string? lastAnimatedVmId;
    private QEMU附加功能? toolsView;

    public 主窗()
    {
        应用日志.写("主窗 constructor begin");
        InitializeComponent();
        RootGrid.Loaded += (_, _) => 按钮交互动画.启用(RootGrid);
        应用日志.写("主窗 XAML initialized");
        Title = "QemuWG";
        sessions = new QEMU会话(qemuSvc);
        sessions.状态变化 += Sessions_StateChanged;

        var windowHandle = WindowNative.GetWindowHandle(this);
        Closed += (_, _) => StopDisplay();
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(1180, 760));
        appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        appWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
        应用日志.写("AppWindow configured");

        Activated += 主窗_Activated;
        应用日志.写("主窗 constructor end");
    }

    public ObservableCollection<虚拟机配置> Machines { get; } = [];
    public ObservableCollection<设备摘要> DeviceSummaries { get; } = [];

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var sidebarWidth = Math.Max(180, e.NewSize.Width * 0.18);
        SidebarColumn.Width = new GridLength(sidebarWidth);
        SidebarTitleText.Visibility = sidebarWidth >= 240 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void 主窗_Activated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= 主窗_Activated;
        try
        {
            await InitializeAsync();
        }
        catch (Exception exception)
        {
            应用日志.写("主窗 initialization failed: " + exception);
            LoadingView.Visibility = Visibility.Collapsed;
            EmptyView.Visibility = Visibility.Visible;
            _ = 页面过渡动画.渐进显示(EmptyView, 9);
            await ShowMessageAsync(T("dialog.operationFailed", "操作失败"), exception.Message);
        }
    }

    private async Task InitializeAsync()
    {
        应用日志.写("InitializeAsync begin");
        var qemuTask = qemuSvc.检测();
        var machinesTask = vmRepo.加载全部();
        await Task.WhenAll(qemuTask, machinesTask);
        应用日志.写("InitializeAsync data loaded");
        qemu = qemuTask.Result;
        QemuVersionText.Text = qemu.Version;
        应用日志.写("InitializeAsync version assigned");
        NewVmButton.IsEnabled = qemu.IsAvailable;

        foreach (var vm in machinesTask.Result) Machines.Add(vm);
        应用日志.写($"InitializeAsync machines assigned: {Machines.Count}");
        LoadingView.Visibility = Visibility.Collapsed;
        if (Machines.Count == 0)
        {
            EmptyView.Visibility = Visibility.Visible;
            _ = 页面过渡动画.渐进显示(EmptyView, 9);
            return;
        }

        VmList.SelectedIndex = 0;
    }

    private async void NewVmButton_Click(object sender, RoutedEventArgs e)
    {
        if (!qemu.IsAvailable)
        {
            await ShowMessageAsync(T("dialog.qemuMissingTitle", "未找到 QEMU"), T("dialog.qemuMissingMessage", "请先安装 QEMU，或将 QEMU 目录加入 PATH。"));
            return;
        }

        var dialog = new 虚拟机编辑(WindowNative.GetWindowHandle(this), qemu, qemuSvc, vmRepo.根目录)
        {
            XamlRoot = RootXamlRoot
        };
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;

        NewVmButton.IsEnabled = false;
        try
        {
            var (result, vm) = await vmRepo.创建(qemu, dialog.BuildMachine(), dialog.ParentDir);
            if (!result.Succeeded || vm is null)
            {
                await ShowOperationErrorAsync(result);
                return;
            }

            InsertSorted(vm);
            VmList.SelectedItem = vm;
        }
        finally
        {
            NewVmButton.IsEnabled = true;
        }
    }

    private void Sessions_StateChanged(object? sender, 虚拟机配置 vm)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            vm.IsRunning = sessions.存在QMP会话(vm);
            if (ReferenceEquals(selectedVm, vm)) RefreshDetails();
        });
    }

    private XamlRoot RootXamlRoot => ((FrameworkElement)Content).XamlRoot;

    private async Task ShowOperationErrorAsync(操作结果 result)
    {
        var content = string.IsNullOrWhiteSpace(result.Detail) ? result.Message : result.Message + Environment.NewLine + Environment.NewLine + result.Detail;
        await ShowMessageAsync(T("dialog.operationFailed", "操作失败"), content);
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootXamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 },
            CloseButtonText = T("common.close", "关闭")
        };
        await ShowDialogAsync(dialog);
    }

    private void InsertSorted(虚拟机配置 vm)
    {
        var index = 0;
        while (index < Machines.Count && string.Compare(Machines[index].Name, vm.Name, StringComparison.CurrentCultureIgnoreCase) < 0) index++;
        Machines.Insert(index, vm);
    }

    private static void CopyEditableValues(虚拟机配置 source, 虚拟机配置 target)
    {
        target.Name = source.Name;
        target.IsoPath = source.IsoPath;
        target.Arch = source.Arch;
        target.Firmware = source.Firmware;
        target.MachineType = source.MachineType;
        target.Accelerator = source.Accelerator;
        target.CpuModel = source.CpuModel;
        target.DisplayBackend = source.DisplayBackend;
        target.VideoDevice = source.VideoDevice;
        target.AudioBackend = source.AudioBackend;
        target.AudioDevice = source.AudioDevice;
        target.NetworkMode = source.NetworkMode;
        target.NetworkModel = source.NetworkModel;
        target.BootOrder = source.BootOrder;
        target.RtcBase = source.RtcBase;
        target.ExtraArgs = source.ExtraArgs;
        target.EnableGuestAgent = source.EnableGuestAgent;
        target.QemuOpts = source.QemuOpts.Select(option => new QEMU选项 { Name = option.Name, Value = option.Value }).ToList();
        target.Devices = source.Devices.Select(device => new QEMU设备
        {
            Model = device.Model,
            Properties = new Dictionary<string, string>(device.Properties, StringComparer.OrdinalIgnoreCase)
        }).ToList();
        target.MemoryMb = source.MemoryMb;
        target.CpuCount = source.CpuCount;
    }

    private static string FormatMemory(int megabytes) => megabytes >= 1024 && megabytes % 1024 == 0
        ? $"{megabytes / 1024} GB"
        : $"{megabytes} MB";

    private static string RawOrDefault(string value, string fallback = "default") => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
