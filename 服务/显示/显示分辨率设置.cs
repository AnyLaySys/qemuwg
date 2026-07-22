namespace QemuWG.服务;

internal static class 显示分辨率设置
{
    private static readonly HashSet<string> 支持分辨率的显卡 = new(StringComparer.OrdinalIgnoreCase)
    {
        "ati-vga", "bochs-display", "qxl", "qxl-vga", "secondary-vga", "VGA",
        "virtio-gpu-device", "virtio-gpu-gl-device", "virtio-gpu-gl-pci", "virtio-gpu-pci",
        "virtio-vga", "virtio-vga-gl"
    };

    public static bool 支持(string videoDevice)
    {
        var model = videoDevice.Split(',', 2, StringSplitOptions.TrimEntries)[0];
        return 支持分辨率的显卡.Contains(model);
    }

    public static string 应用(string videoDevice, int width, int height)
    {
        if (width <= 0 || height <= 0 || !支持(videoDevice)) return videoDevice;

        var parts = videoDevice.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.StartsWith("xres=", StringComparison.OrdinalIgnoreCase)
                           && !part.StartsWith("yres=", StringComparison.OrdinalIgnoreCase));
        return string.Join(',', parts.Append($"xres={width}").Append($"yres={height}"));
    }
}
