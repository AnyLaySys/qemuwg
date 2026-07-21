using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QemuWG.数据;
using QemuWG.界面;
using WinRT.Interop;

namespace QemuWG;

public sealed partial class 主窗
{
    private async void GlobalDiskManager_Click(object sender, RoutedEventArgs e)
    {
        await ShowDiskManagerAsync(selectedVm ?? new 虚拟机配置 { Name = T("disk.manager", "磁盘管理") });
    }

    private async void QemuTools_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            应用日志.写("Opening embedded QEMU工具界面");
            ShowToolsView();
        }
        catch (Exception exception)
        {
            应用日志.写("QemuToolsDialog failed: " + exception);
            await ShowMessageAsync(
                T("dialog.operationFailed", "操作失败"),
                string.Format(T("tools.openFailed", "无法打开 QEMU 工具箱：{0}"), exception.Message));
        }
    }

    private void ShowToolsView()
    {
        StopDisplay();
        LoadingView.Visibility = Visibility.Collapsed;
        EmptyView.Visibility = Visibility.Collapsed;
        DetailsView.Visibility = Visibility.Collapsed;
        toolsView ??= new QEMU工具界面(WindowNative.GetWindowHandle(this), qemu, toolSessions);
        ToolsHost.Content = toolsView;
        ToolsHost.Visibility = Visibility.Visible;
    }

    private async Task ShowDiskManagerAsync(虚拟机配置 machine)
    {
        try
        {
            应用日志.写("Opening 磁盘管理");
            var dialog = new 磁盘管理(WindowNative.GetWindowHandle(this), qemu, machine)
            {
                XamlRoot = RootXamlRoot
            };
            await ShowDialogAsync(dialog);
            应用日志.写("磁盘管理 closed");
            RefreshDetails();
        }
        catch (Exception exception)
        {
            应用日志.写("磁盘管理 failed: " + exception);
            await ShowMessageAsync(
                T("dialog.operationFailed", "操作失败"),
                string.Format(T("disk.openFailed", "无法打开磁盘管理：{0}"), exception.Message));
        }
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 12, MinWidth = 460 };
        panel.Children.Add(CreateSettingsRow("QEMU", qemu.Version, qemu.RootDir));
        panel.Children.Add(CreateSettingsRow(T("settings.library", "虚拟机库"), "Documents\\qemuwg\\vm", vmRepo.根目录));
        var dialog = new ContentDialog
        {
            XamlRoot = RootXamlRoot,
            Title = T("settings.title", "设置"),
            Content = panel,
            CloseButtonText = T("common.done", "完成")
        };
        await ShowDialogAsync(dialog);
    }

    private FrameworkElement CreateSettingsRow(string title, string value, string path)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        var details = new StackPanel { Spacing = 1 };
        details.Children.Add(new TextBlock { Text = value });
        details.Children.Add(new TextBlock
        {
            Text = path,
            FontSize = 11,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 120, 126, 135)),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(details, 1);
        grid.Children.Add(details);
        var button = new Button { Content = new SymbolIcon(Symbol.Folder), CornerRadius = new CornerRadius(6) };
        button.Click += (_, _) => OpenPath(path);
        Grid.SetColumn(button, 2);
        grid.Children.Add(button);
        return grid;
    }

}
