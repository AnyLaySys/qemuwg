using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QemuWG.data;
using QemuWG.ui;
using WinRT.Interop;

namespace QemuWG;

public sealed partial class MainWindow
{
    private async void GlobalDiskManager_Click(object sender, RoutedEventArgs e)
    {
        await ShowDiskManagerAsync(selectedVm ?? new VmCfg { Name = T("workspace.disk", "磁盘工作区") });
    }

    private async void QemuTools_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppLog.Write("Opening embedded QemuToolsView");
            ShowToolsView();
        }
        catch (Exception exception)
        {
            AppLog.Write("QemuToolsDialog failed: " + exception);
            await ShowMessageAsync(
                T("dialog.operationFailed", "操作失败"),
                string.Format(T("tools.openFailed", "无法打开 QEMU 工具箱：{0}"), exception.Message));
        }
    }

    private void ShowToolsView()
    {
        LoadingView.Visibility = Visibility.Collapsed;
        EmptyView.Visibility = Visibility.Collapsed;
        DetailsView.Visibility = Visibility.Collapsed;
        toolsView ??= new QemuToolsView(WindowNative.GetWindowHandle(this), qemu, toolSessions);
        ToolsHost.Content = toolsView;
        ToolsHost.Visibility = Visibility.Visible;
    }

    private async Task ShowDiskManagerAsync(VmCfg machine)
    {
        try
        {
            AppLog.Write("Opening DiskMgrDialog");
            var dialog = new DiskMgrDialog(WindowNative.GetWindowHandle(this), qemu, machine)
            {
                XamlRoot = RootXamlRoot
            };
            await dialog.ShowAsync();
            AppLog.Write("DiskMgrDialog closed");
            RefreshDetails();
        }
        catch (Exception exception)
        {
            AppLog.Write("DiskMgrDialog failed: " + exception);
            await ShowMessageAsync(
                T("dialog.operationFailed", "操作失败"),
                string.Format(T("disk.openFailed", "无法打开磁盘管理：{0}"), exception.Message));
        }
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 12, MinWidth = 460 };
        panel.Children.Add(CreateSettingsRow("QEMU", qemu.Version, qemu.RootDir));
        panel.Children.Add(CreateSettingsRow(T("settings.library", "虚拟机库"), "Documents\\qemuwg\\vm", vmRepo.RootPath));
        var dialog = new ContentDialog
        {
            XamlRoot = RootXamlRoot,
            Title = T("settings.title", "设置"),
            Content = panel,
            CloseButtonText = T("common.done", "完成")
        };
        await dialog.ShowAsync();
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




