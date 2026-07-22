using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using QemuWG.数据;

namespace QemuWG.界面;

public sealed class 仿真侧栏项 : INotifyPropertyChanged, IDisposable
{
    public 仿真侧栏项(仿真配置 仿真)
    {
        this.仿真 = 仿真;
        仿真.PropertyChanged += 仿真属性变化;
        更新配色();
    }

    public 仿真配置 仿真 { get; }
    public string 名称 => 仿真.Name;
    public string 状态 => 仿真.StatusText;
    public string 首字母 => 获取首字母(仿真.Name);
    public Brush 背景 { get; private set; } = new SolidColorBrush();
    public Brush 前景 { get; private set; } = new SolidColorBrush();

    public void 刷新()
    {
        更新配色();
        OnPropertyChanged(nameof(名称));
        OnPropertyChanged(nameof(状态));
        OnPropertyChanged(nameof(首字母));
        OnPropertyChanged(nameof(背景));
        OnPropertyChanged(nameof(前景));
    }

    public void Dispose() => 仿真.PropertyChanged -= 仿真属性变化;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void 仿真属性变化(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(仿真配置.StatusText)) OnPropertyChanged(nameof(状态));
    }

    private void 更新配色()
    {
        var 系统标识 = 仿真系统标识.解析(仿真.来宾系统, 仿真.Name, 仿真.IsoPath);
        var 配色 = 仿真系统主题.获取(系统标识);
        背景 = new SolidColorBrush(配色.背景色);
        前景 = new SolidColorBrush(配色.前景色);
    }

    private static string 获取首字母(string? 名称)
    {
        var 文本 = 名称?.Trim();
        if (string.IsNullOrEmpty(文本)) return "?";
        return StringInfo.GetNextTextElement(文本).ToUpperInvariant();
    }

    private void OnPropertyChanged([CallerMemberName] string? 属性名 = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(属性名));
}
