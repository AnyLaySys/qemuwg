using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QemuWG.data;
using QemuWG.svc;
using QemuWG.ui;
using WinRT.Interop;

namespace QemuWG;

public sealed partial class MainWindow : Window
{
    private static string T(string key, string fallback) => LocaleSvc.Current.Get(key, fallback);

    private readonly QemuSvc qemuSvc = new();
    private readonly VmRepo vmRepo = new();
    private readonly QemuSessionMgr sessions;
    private readonly QemuToolSessionMgr toolSessions = new();
    private QemuInstall qemu = new();
    private VmCfg? selectedVm;
    private QemuToolsView? toolsView;

    public MainWindow()
    {
        AppLog.Write("MainWindow constructor begin");
        InitializeComponent();
        AppLog.Write("MainWindow XAML initialized");
        Title = "QemuWG";
        sessions = new QemuSessionMgr(qemuSvc);
        sessions.StateChanged += Sessions_StateChanged;

        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this));
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(1180, 760));
        appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        appWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
        AppLog.Write("AppWindow configured");

        Activated += MainWindow_Activated;
        AppLog.Write("MainWindow constructor end");
    }

    public ObservableCollection<VmCfg> Machines { get; } = [];
    public ObservableCollection<DeviceSummary> DeviceSummaries { get; } = [];

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var sidebarWidth = Math.Max(180, e.NewSize.Width * 0.18);
        SidebarColumn.Width = new GridLength(sidebarWidth);
        SidebarTitleText.Visibility = sidebarWidth >= 240 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= MainWindow_Activated;
        try
        {
            await InitializeAsync();
        }
        catch (Exception exception)
        {
            AppLog.Write("MainWindow initialization failed: " + exception);
            LoadingView.Visibility = Visibility.Collapsed;
            EmptyView.Visibility = Visibility.Visible;
            await ShowMessageAsync(T("dialog.operationFailed", "操作失败"), exception.Message);
        }
    }

    private async Task InitializeAsync()
    {
        AppLog.Write("InitializeAsync begin");
        var qemuTask = qemuSvc.DetectAsync();
        var machinesTask = vmRepo.LoadAllAsync();
        await Task.WhenAll(qemuTask, machinesTask);
        AppLog.Write("InitializeAsync data loaded");
        qemu = qemuTask.Result;
        QemuVersionText.Text = qemu.Version;
        AppLog.Write("InitializeAsync version assigned");
        NewVmButton.IsEnabled = qemu.IsAvailable;

        foreach (var vm in machinesTask.Result) Machines.Add(vm);
        AppLog.Write($"InitializeAsync machines assigned: {Machines.Count}");
        LoadingView.Visibility = Visibility.Collapsed;
        if (Machines.Count == 0)
        {
            EmptyView.Visibility = Visibility.Visible;
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

        var dialog = new VmEditorDialog(WindowNative.GetWindowHandle(this), qemu, qemuSvc, vmRepo.RootPath)
        {
            XamlRoot = RootXamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        NewVmButton.IsEnabled = false;
        try
        {
            var (result, vm) = await vmRepo.CreateAsync(qemu, dialog.BuildMachine(), dialog.ParentDir);
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

    private void Sessions_StateChanged(object? sender, VmCfg vm)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            vm.IsRunning = sessions.HasQmpSession(vm);
            if (ReferenceEquals(selectedVm, vm)) RefreshDetails();
        });
    }

    private XamlRoot RootXamlRoot => ((FrameworkElement)Content).XamlRoot;

    private async Task ShowOperationErrorAsync(OperationResult result)
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
        await dialog.ShowAsync();
    }

    private void InsertSorted(VmCfg vm)
    {
        var index = 0;
        while (index < Machines.Count && string.Compare(Machines[index].Name, vm.Name, StringComparison.CurrentCultureIgnoreCase) < 0) index++;
        Machines.Insert(index, vm);
    }

    private static void CopyEditableValues(VmCfg source, VmCfg target)
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
        target.QemuOpts = source.QemuOpts.Select(option => new QemuOptionEntry { Name = option.Name, Value = option.Value }).ToList();
        target.Devices = source.Devices.Select(device => new QemuDeviceEntry
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


