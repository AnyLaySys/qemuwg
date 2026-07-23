using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
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
    private readonly 仿真仓库 vmRepo = new();
    private readonly QEMU会话 sessions;
    private readonly QEMU工具会话 toolSessions = new();
    private readonly Dictionary<string, QEMU附加功能> vmFeatureViews = new(StringComparer.Ordinal);
    private readonly AppWindow appWindow;
    private QEMU安装 qemu = new();
    private 仿真配置? selectedVm;
    private string? lastAnimatedVmId;
    private bool? compactDetailLayout;
    private readonly DispatcherQueueTimer resizeAnimationTimer;
    private bool resizeAnimationReady;
    private OverlappedPresenterState? presenterState;

    public 主窗()
    {
        应用日志.写("主窗 constructor begin");
        InitializeComponent();
        scrollActivationGuard = new 滚动激活保护(DispatcherQueue);
        初始化电源按钮指针监听();
        DisplaySurface.SizeChanged += (_, _) => 更新内嵌画面布局();
        VmItems.ItemsSource = 仿真侧栏项列表;
        resizeAnimationTimer = DispatcherQueue.CreateTimer();
        resizeAnimationTimer.Interval = TimeSpan.FromMilliseconds(140);
        resizeAnimationTimer.IsRepeating = false;
        resizeAnimationTimer.Tick += (_, _) =>
        {
            if (DetailsView.Visibility == Visibility.Visible)
                页面过渡动画.布局稳定(VmSummaryColumn, VmContentColumn);
            else if (EmptyView.Visibility == Visibility.Visible)
                页面过渡动画.布局稳定(EmptyView);
        };
        RootGrid.Loaded += (_, _) =>
        {
            按钮交互动画.启用(RootGrid);
        };
        应用日志.写("主窗 XAML initialized");
        Title = "QemuWG";
        sessions = new QEMU会话(qemuSvc);
        sessions.状态变化 += Sessions_StateChanged;

        var windowHandle = WindowNative.GetWindowHandle(this);
        Closed += (_, _) =>
        {
            scrollActivationGuard.停止();
            StopDisplay();
            toolSessions.停止全部();
        };
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(1180, 760));
        appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        if (appWindow.Presenter is OverlappedPresenter presenter) presenterState = presenter.State;
        appWindow.Changed += AppWindow_Changed;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        appWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
        应用日志.写("AppWindow configured");

        Activated += 主窗_Activated;
        Activated += 主窗_滚动激活状态变化;
        应用日志.写("主窗 constructor end");
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (sender.Presenter is not OverlappedPresenter presenter || presenterState == presenter.State) return;
        presenterState = presenter.State;
        if (presenter.State == OverlappedPresenterState.Minimized) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            resizeAnimationTimer.Stop();
            resizeAnimationTimer.Start();
        });
    }

    public ObservableCollection<设备摘要> DeviceSummaries { get; } = [];

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
        DetailBodyGrid.MinHeight = Math.Max(360, e.NewSize.Height - 105);
        var detailWidth = Math.Max(0, e.NewSize.Width - SidebarColumn.ActualWidth);
        var compact = detailWidth < 540;
        配置详情布局(compact);
        if (!resizeAnimationReady)
        {
            resizeAnimationReady = true;
            return;
        }
        resizeAnimationTimer.Stop();
        resizeAnimationTimer.Start();
    }

    private void 配置详情布局(bool compact)
    {
        if (compactDetailLayout == compact) return;
        compactDetailLayout = compact;
        EmbeddedDisplayHost.VerticalAlignment = compact
            ? VerticalAlignment.Bottom
            : VerticalAlignment.Top;
        DetailBodyGrid.RowDefinitions.Clear();
        ConfigurationCard.Visibility = Visibility.Visible;
        if (compact)
        {
            DetailBodyGrid.ColumnSpacing = 0;
            DetailLeftColumn.Width = new GridLength(1, GridUnitType.Star);
            DetailRightColumn.Width = new GridLength(0);
            DetailBodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            DetailBodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(VmSummaryColumn, 0);
            Grid.SetColumn(VmSummaryColumn, 0);
            VmSummaryColumn.MinHeight = 420;
            Grid.SetColumn(VmContentColumn, 0);
            Grid.SetRow(VmContentColumn, 1);
            VmContentColumn.MinHeight = 360;
            return;
        }

        DetailBodyGrid.ColumnSpacing = 14;
        DetailLeftColumn.Width = new GridLength(0.32, GridUnitType.Star);
        DetailRightColumn.Width = new GridLength(0.68, GridUnitType.Star);
        DetailBodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(VmSummaryColumn, 0);
        Grid.SetColumn(VmSummaryColumn, 0);
        VmSummaryColumn.MinHeight = 0;
        Grid.SetColumn(VmContentColumn, 1);
        Grid.SetRow(VmContentColumn, 0);
        VmContentColumn.MinHeight = 0;
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
        QemuVersionText.Text = 简化QEMU版本(qemu.Version);
        应用日志.写("InitializeAsync version assigned");
        NewVmButton.IsEnabled = qemu.IsAvailable;

        foreach (var vm in machinesTask.Result) 插入仿真(vm);
        应用日志.写($"InitializeAsync VMs assigned: {仿真侧栏项列表.Count}");
        LoadingView.Visibility = Visibility.Collapsed;
        if (仿真侧栏项列表.Count == 0)
        {
            EmptyView.Visibility = Visibility.Visible;
            _ = 页面过渡动画.渐进显示(EmptyView, 9);
            return;
        }

        选择仿真(仿真侧栏项列表[0]);
    }

    private async void NewVmButton_Click(object sender, RoutedEventArgs e)
    {
        if (!qemu.IsAvailable)
        {
            await ShowMessageAsync(T("dialog.qemuMissingTitle", "未找到 QEMU"), T("dialog.qemuMissingMessage", "请先安装 QEMU，或将 QEMU 目录加入 PATH。"));
            return;
        }

        var dialog = new 仿真编辑(WindowNative.GetWindowHandle(this), qemu, qemuSvc, vmRepo.根目录)
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

            选择仿真(插入仿真(vm));
        }
        finally
        {
            NewVmButton.IsEnabled = true;
        }
    }

    private void Sessions_StateChanged(object? sender, 仿真配置 vm)
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

    private static void CopyEditableValues(仿真配置 source, 仿真配置 target)
    {
        target.Name = source.Name;
        target.DiskPath = source.DiskPath;
        target.DiskFormat = source.DiskFormat;
        target.DiskMode = source.DiskMode;
        target.DiskGb = source.DiskGb;
        target.IsoPath = source.IsoPath;
        target.来宾系统 = source.来宾系统;
        target.Arch = source.Arch;
        target.Firmware = source.Firmware;
        target.MachineType = source.MachineType;
        target.Accelerator = source.Accelerator;
        target.CpuModel = source.CpuModel;
        target.DisplayBackend = source.DisplayBackend;
        target.VideoDevice = source.VideoDevice;
        target.DisplayWidth = source.DisplayWidth;
        target.DisplayHeight = source.DisplayHeight;
        target.AudioBackend = source.AudioBackend;
        target.AudioDevice = source.AudioDevice;
        target.KeyboardDevice = source.KeyboardDevice;
        target.MouseDevice = source.MouseDevice;
        target.NetworkMode = source.NetworkMode;
        target.NetworkModel = source.NetworkModel;
        target.NetworkMac = source.NetworkMac;
        target.HostForwarding = source.HostForwarding;
        target.DiskInterface = source.DiskInterface;
        target.DiskCache = source.DiskCache;
        target.DiskAio = source.DiskAio;
        target.DiskDiscard = source.DiskDiscard;
        target.DiskDetectZeroes = source.DiskDetectZeroes;
        target.BootOrder = source.BootOrder;
        target.BootOnce = source.BootOnce;
        target.RtcBase = source.RtcBase;
        target.KeyboardLayout = source.KeyboardLayout;
        target.ExtraArgs = source.ExtraArgs;
        target.BootMenu = source.BootMenu;
        target.SnapshotMode = source.SnapshotMode;
        target.StartPaused = source.StartPaused;
        target.QemuOpts = source.QemuOpts.Select(option => new QEMU选项 { Name = option.Name, Value = option.Value }).ToList();
        target.Devices = source.Devices.Select(device => new QEMU设备
        {
            Model = device.Model,
            Properties = new Dictionary<string, string>(device.Properties, StringComparer.OrdinalIgnoreCase)
        }).ToList();
        target.PhysicalStorage = source.PhysicalStorage.Select(storage => new 物理存储挂载
        {
            DevicePath = storage.DevicePath,
            DisplayName = storage.DisplayName,
            Interface = storage.Interface,
            ReadOnly = storage.ReadOnly,
            Kind = storage.Kind,
            DiskNumber = storage.DiskNumber,
            PartitionNumber = storage.PartitionNumber,
            Offset = storage.Offset,
            Size = storage.Size,
            UniqueId = storage.UniqueId
        }).ToList();
        target.MemoryMb = source.MemoryMb;
        target.CpuCount = source.CpuCount;
        target.CpuSockets = source.CpuSockets;
        target.CpuCores = source.CpuCores;
        target.CpuThreads = source.CpuThreads;
    }

    private static string FormatMemory(int megabytes) => megabytes >= 1024 && megabytes % 1024 == 0
        ? $"{megabytes / 1024} GB"
        : $"{megabytes} MB";

    private static string RawOrDefault(string value, string fallback = "default") => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string 简化QEMU版本(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return "QEMU";
        var detailsStart = version.IndexOf(" (", StringComparison.Ordinal);
        var conciseVersion = detailsStart > 0 ? version[..detailsStart] : version;
        return conciseVersion.StartsWith("QEMU ", StringComparison.OrdinalIgnoreCase)
            ? conciseVersion[5..]
            : conciseVersion;
    }

    private static void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
