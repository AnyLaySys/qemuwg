using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.服务;

namespace QemuWG.界面;

public sealed partial class 仿真编辑
{
    private bool updatingResolutionControls;

    private void 初始化分辨率(int width, int height)
    {
        updatingResolutionControls = true;
        try
        {
            DisplayWidthBox.Value = Math.Max(0, width);
            DisplayHeightBox.Value = Math.Max(0, height);
            选择分辨率预设(width, height);
        }
        finally
        {
            updatingResolutionControls = false;
        }
    }

    private void VideoCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CapabilityCombo_SelectionChanged(sender, e);
        更新分辨率可用性();
    }

    private void VideoCombo_LostFocus(object sender, RoutedEventArgs e) => 更新分辨率可用性();

    private void 更新分辨率可用性()
    {
        var videoDevice = CurrentComboValue(VideoCombo);
        var explicitDevice = !string.IsNullOrWhiteSpace(videoDevice)
                             && !string.Equals(videoDevice, "auto", StringComparison.OrdinalIgnoreCase);
        var supported = explicitDevice && 显示分辨率设置.支持(videoDevice);
        ResolutionPresetCombo.IsEnabled = supported;
        DisplayWidthBox.IsEnabled = supported;
        DisplayHeightBox.IsEnabled = supported;
        ResolutionSupportText.Text = supported
            ? string.Empty
            : explicitDevice
                ? T("vmEditor.resolutionUnsupported", "当前显卡不公开 xres/yres 属性，保持由来宾系统自动决定。")
                : T("vmEditor.resolutionSelectVideo", "明确选择支持分辨率设置的显卡后可用。");
    }

    private void ResolutionPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingResolutionControls || ResolutionPresetCombo.SelectedItem is not ComboBoxItem item) return;
        var value = item.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(value) || value == "custom") return;
        var dimensions = value.Split('x');
        if (dimensions.Length != 2
            || !int.TryParse(dimensions[0], out var width)
            || !int.TryParse(dimensions[1], out var height)) return;

        updatingResolutionControls = true;
        try
        {
            DisplayWidthBox.Value = width;
            DisplayHeightBox.Value = height;
        }
        finally
        {
            updatingResolutionControls = false;
        }
    }

    private void DisplayResolutionBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (updatingResolutionControls) return;
        updatingResolutionControls = true;
        try
        {
            选择分辨率预设(读取分辨率值(DisplayWidthBox.Value), 读取分辨率值(DisplayHeightBox.Value));
        }
        finally
        {
            updatingResolutionControls = false;
        }
    }

    private void 选择分辨率预设(int width, int height)
    {
        var tag = $"{width}x{height}";
        ResolutionPresetCombo.SelectedItem = ResolutionPresetCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            ?? ResolutionPresetCombo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), "custom", StringComparison.OrdinalIgnoreCase));
    }

    private static int 读取分辨率值(double value) =>
        double.IsFinite(value) && value > 0 ? checked((int)value) : 0;
}
