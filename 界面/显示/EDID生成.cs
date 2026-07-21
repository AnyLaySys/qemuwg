using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace QemuWG.界面;

public sealed partial class QEMU附加功能
{
    private async void BrowseEdidOutput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedFileName = "display", DefaultFileExtension = ".bin" };
        InitializeWithWindow.Initialize(picker, ownerHandle);
        picker.FileTypeChoices.Add(T("tools.edidFile", "EDID 文件"), [".bin", ".edid"]);
        var file = await picker.PickSaveFileAsync();
        if (file is not null) EdidOutputBox.Text = file.Path;
    }

    private async void GenerateEdidButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(EdidOutputBox.Text)) { EdidResultBox.Text = T("tools.selectOutput", "请选择输出文件。"); return; }
        EdidProgress.IsActive = true;
        EdidProgress.Visibility = Visibility.Visible;
        try
        {
            var result = await tools.生成EDID(install, BuildEdidArguments());
            EdidResultBox.Text = result.退出码 == 0 ? T("tools.generated", "EDID 已生成。") + Environment.NewLine + EdidOutputBox.Text : FormatResult(result);
        }
        catch (Exception exception) { EdidResultBox.Text = exception.ToString(); }
        finally { EdidProgress.IsActive = false; EdidProgress.Visibility = Visibility.Collapsed; }
    }

    private IEnumerable<string> BuildEdidArguments()
    {
        yield return "-o"; yield return EdidOutputBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(EdidVendorBox.Text)) { yield return "-v"; yield return EdidVendorBox.Text.Trim(); }
        if (!string.IsNullOrWhiteSpace(EdidNameBox.Text)) { yield return "-n"; yield return EdidNameBox.Text.Trim(); }
        if (!string.IsNullOrWhiteSpace(EdidSerialBox.Text)) { yield return "-s"; yield return EdidSerialBox.Text.Trim(); }
        foreach (var pair in new[] { ("-d", EdidDpiBox.Value), ("-x", EdidPreferredWidthBox.Value), ("-y", EdidPreferredHeightBox.Value), ("-X", EdidMaximumWidthBox.Value), ("-Y", EdidMaximumHeightBox.Value) })
            if (!double.IsNaN(pair.Value) && pair.Value > 0) { yield return pair.Item1; yield return ((int)pair.Value).ToString(); }
    }
}
