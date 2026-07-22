using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.数据;
using QemuWG.界面;

namespace QemuWG;

public sealed partial class 主窗
{
    public ObservableCollection<仿真侧栏项> 仿真侧栏项列表 { get; } = [];

    private void VmSidebarButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: 仿真侧栏项 item }) 选择仿真(item);
    }

    private void 选择仿真(仿真侧栏项 item)
    {
        selectedVm = item.仿真;
        RefreshDetails();
    }

    private 仿真侧栏项 插入仿真(仿真配置 仿真)
    {
        var 项 = new 仿真侧栏项(仿真);
        var 索引 = 查找插入位置(项.名称);
        仿真侧栏项列表.Insert(索引, 项);
        return 项;
    }

    private void 刷新并重排仿真(仿真配置 仿真)
    {
        var 项 = 仿真侧栏项列表.FirstOrDefault(候选 => ReferenceEquals(候选.仿真, 仿真));
        if (项 is null)
        {
            选择仿真(插入仿真(仿真));
            return;
        }

        仿真侧栏项列表.Remove(项);
        项.刷新();
        仿真侧栏项列表.Insert(查找插入位置(项.名称), 项);
        选择仿真(项);
    }

    private void 移除仿真(仿真配置 仿真)
    {
        var 项 = 仿真侧栏项列表.FirstOrDefault(候选 => ReferenceEquals(候选.仿真, 仿真));
        if (项 is null) return;
        仿真侧栏项列表.Remove(项);
        项.Dispose();
    }

    private int 查找插入位置(string 名称)
    {
        var 索引 = 0;
        while (索引 < 仿真侧栏项列表.Count
               && string.Compare(仿真侧栏项列表[索引].名称, 名称, StringComparison.CurrentCultureIgnoreCase) < 0)
            索引++;
        return 索引;
    }
}
