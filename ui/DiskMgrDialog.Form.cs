using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QemuWG.data;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace QemuWG.ui;

public sealed partial class DiskMgrDialog
{
    private void BuildForm(DiskCmdDef command)
    {
        inputs.Clear();
        fieldContainers.Clear();
        FormGrid.Children.Clear();
        FormGrid.RowDefinitions.Clear();
        AdvancedFieldsPanel.Children.Clear();
        awaitingConfirmation = false;
        SafetyInfo.IsOpen = false;
        ExecuteButtonText.Text = T("common.execute", "执行");
        OutputBox.Text = string.Empty;
        ExitCodeText.Text = command.Description;
        CommandIcon.Glyph = command.Glyph;
        CommandIcon.Foreground = command.IconBrush;
        CommandTitleText.Text = command.DisplayName;
        CommandDescriptionText.Text = command.Description;

        var row = 0;
        var column = 0;
        foreach (var field in command.Fields)
        {
            if (field.Advanced)
            {
                var advancedContainer = CreateField(field);
                AdvancedFieldsPanel.Children.Add(advancedContainer);
                fieldContainers[field.Id] = advancedContainer;
                continue;
            }
            if (field.Wide && column != 0)
            {
                row++;
                column = 0;
            }
            while (FormGrid.RowDefinitions.Count <= row)
                FormGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var container = CreateField(field);
            Grid.SetRow(container, row);
            Grid.SetColumn(container, column);
            if (field.Wide)
            {
                Grid.SetColumnSpan(container, 2);
                row++;
                column = 0;
            }
            else if (column == 0)
            {
                column = 1;
            }
            else
            {
                row++;
                column = 0;
            }
            FormGrid.Children.Add(container);
            fieldContainers[field.Id] = container;
        }

        AdvancedExpander.Visibility = AdvancedFieldsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        UpdateDependencies();
        UpdatePreview();
        ExecuteButton.IsEnabled = !(machine.IsRunning && command.CanWrite);
        if (!ExecuteButton.IsEnabled)
        {
            SafetyInfo.Message = T("disk.runningBlocked", "虚拟机运行时禁止执行会修改磁盘的命令。");
            SafetyInfo.IsOpen = true;
        }
    }

    private FrameworkElement CreateField(DiskFieldDef field)
    {
        if (field.Mode == DiskFieldMode.Flag)
        {
            var toggle = new ToggleSwitch
            {
                Header = field.Label,
                OnContent = T("common.on", "开"),
                OffContent = T("common.off", "关"),
                IsOn = string.Equals(field.DefaultValue, "true", StringComparison.OrdinalIgnoreCase)
            };
            toggle.Toggled += InputChanged;
            inputs[field.Id] = toggle;
            return toggle;
        }

        var panel = new StackPanel { Spacing = 3 };
        panel.Children.Add(new TextBlock
        {
            Text = field.Label,
            FontSize = 11
        });

        if (field.Choices.Count > 0)
        {
            var combo = new ComboBox
            {
                ItemsSource = field.Choices.Select(value => new ChoiceItem(value, ChoiceLabel(field, value))).ToList(),
                DisplayMemberPath = nameof(ChoiceItem.Label),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxDropDownHeight = 240
            };
            combo.SelectedItem = ((IReadOnlyList<ChoiceItem>)combo.ItemsSource)
                .FirstOrDefault(item => item.Value == field.DefaultValue) ?? ((IReadOnlyList<ChoiceItem>)combo.ItemsSource).FirstOrDefault();
            combo.SelectionChanged += InputChanged;
            panel.Children.Add(combo);
            inputs[field.Id] = combo;
            return panel;
        }

        var textBox = new TextBox { Text = DefaultValue(field) };
        textBox.TextChanged += InputChanged;
        inputs[field.Id] = textBox;
        if (field.PathKind == DiskPathKind.None)
        {
            panel.Children.Add(textBox);
            return panel;
        }

        var pathGrid = new Grid { ColumnSpacing = 6 };
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pathGrid.Children.Add(textBox);
        var browse = new Button { Content = new SymbolIcon(Symbol.Folder), CornerRadius = new CornerRadius(6) };
        browse.Click += async (_, _) => await BrowsePathAsync(field, textBox);
        Grid.SetColumn(browse, 1);
        pathGrid.Children.Add(browse);
        panel.Children.Add(pathGrid);
        return panel;
    }

    private string DefaultValue(DiskFieldDef field)
    {
        if (!string.IsNullOrEmpty(field.DefaultValue)) return field.DefaultValue;
        return field.Id switch
        {
            "file" or "file1" or "sources" or "input" => machine.DiskPath,
            _ => string.Empty
        };
    }

    private string ChoiceLabel(DiskFieldDef field, string value)
    {
        var valueKey = value.TrimStart('-').Replace('-', '.');
        var commandKey = $"disk.choice.{selectedCmd?.Name}.{field.Id}.{valueKey}";
        var genericKey = $"disk.choice.{field.Id}.{valueKey}";
        return T(commandKey, T(genericKey, value));
    }

    private async Task BrowsePathAsync(DiskFieldDef field, TextBox textBox)
    {
        if (field.PathKind == DiskPathKind.Save)
        {
            var picker = new FileSavePicker { SuggestedFileName = "disk" };
            InitializeWithWindow.Initialize(picker, ownerHandle);
            picker.FileTypeChoices.Add(T("disk.imageFileType", "磁盘镜像"), [".qcow2", ".img", ".raw", ".vmdk", ".vhdx"]);
            var file = await picker.PickSaveFileAsync();
            if (file is not null) textBox.Text = file.Path;
            return;
        }

        var openPicker = new FileOpenPicker();
        InitializeWithWindow.Initialize(openPicker, ownerHandle);
        openPicker.FileTypeFilter.Add("*");
        if (field.Mode == DiskFieldMode.MultiPositional)
        {
            var files = await openPicker.PickMultipleFilesAsync();
            if (files.Count > 0) textBox.Text = string.Join(';', files.Select(file => file.Path));
        }
        else
        {
            var file = await openPicker.PickSingleFileAsync();
            if (file is not null) textBox.Text = file.Path;
        }
    }

    private void InputChanged(object sender, object e)
    {
        awaitingConfirmation = false;
        ExecuteButtonText.Text = T("common.execute", "执行");
        if (!(machine.IsRunning && selectedCmd?.CanWrite == true)) SafetyInfo.IsOpen = false;
        UpdateDependencies();
        UpdatePreview();
    }

    private void UpdateDependencies()
    {
        if (selectedCmd is null) return;
        var values = ReadValues();
        foreach (var field in selectedCmd.Fields)
        {
            if (string.IsNullOrEmpty(field.DependsOnId) || !fieldContainers.TryGetValue(field.Id, out var container)) continue;
            values.TryGetValue(field.DependsOnId, out var current);
            var matches = string.Equals(current, field.DependsOnValue, StringComparison.OrdinalIgnoreCase);
            container.Visibility = (field.InvertDependency ? !matches : matches) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdatePreview()
    {
        if (selectedCmd is null) return;
        try
        {
            CommandPreviewBox.Text = qemuImgSvc.BuildArgs(selectedCmd, ReadValues()).Preview;
        }
        catch (InvalidOperationException)
        {
            CommandPreviewBox.Text = "qemu-img " + selectedCmd.Name;
        }
    }

    private Dictionary<string, string> ReadValues()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, control) in inputs)
        {
            values[id] = control switch
            {
                TextBox textBox => textBox.Text,
                ComboBox comboBox => comboBox.SelectedItem is ChoiceItem item ? item.Value : comboBox.SelectedItem?.ToString() ?? string.Empty,
                ToggleSwitch toggle => toggle.IsOn ? "true" : "false",
                _ => string.Empty
            };
        }
        return values;
    }

    private sealed record ChoiceItem(string Value, string Label);
}

