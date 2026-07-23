using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace QemuWG.界面;

public sealed partial class 仿真编辑
{
    private int currentCardLayout;
    private readonly StackPanel peripheralLeftColumn = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Top,
        Spacing = 8
    };
    private readonly StackPanel peripheralRightColumn = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Top,
        Spacing = 8
    };

    private void EditorScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
        var viewportWidth = EditorScrollViewer.ViewportWidth > 0
            ? EditorScrollViewer.ViewportWidth
            : e.NewSize.Width;

        var layout = viewportWidth switch
        {
            >= 1100 => 3,
            >= 700 => 2,
            _ => 1
        };
        if (layout == currentCardLayout) return;
        var horizontalOffset = EditorScrollViewer.HorizontalOffset;
        var verticalOffset = EditorScrollViewer.VerticalOffset;
        var zoomFactor = EditorScrollViewer.ZoomFactor;
        var previousLayout = currentCardLayout;
        currentCardLayout = layout;
        if (previousLayout == 0 || (previousLayout == 1) != (layout == 1))
            ConfigureIdentity(layout == 1);
        PrepareCardContainers(layout);

        switch (layout)
        {
            case 3:
                ConfigureGrid(12, 4);
                AddCard(PlatformCard, 0, 0, 6);
                AddCard(ProcessorCard, 0, 6, 3);
                AddCard(MemoryCard, 0, 9, 3);
                AddCard(StorageCard, 1, 0, 6);
                AddCardColumn(peripheralLeftColumn, 1, 6, 3, DisplayCard, AudioCard);
                AddCardColumn(peripheralRightColumn, 1, 9, 3, NetworkCard, InputCard);
                AddCard(IntegrationCard, 2, 0, 12);
                AddCard(AdvancedCard, 3, 0, 12);
                break;
            case 2:
                ConfigureGrid(2, 6);
                AddCard(PlatformCard, 0, 0, 2);
                AddCard(ProcessorCard, 1, 0, 1);
                AddCard(MemoryCard, 1, 1, 1);
                AddCard(StorageCard, 2, 0, 2);
                AddCardColumn(peripheralLeftColumn, 3, 0, 1, DisplayCard, AudioCard);
                AddCardColumn(peripheralRightColumn, 3, 1, 1, NetworkCard, InputCard);
                AddCard(IntegrationCard, 4, 0, 2);
                AddCard(AdvancedCard, 5, 0, 2);
                break;
            default:
                ConfigureGrid(1, 10);
                AddCard(PlatformCard, 0, 0, 1);
                AddCard(ProcessorCard, 1, 0, 1);
                AddCard(MemoryCard, 2, 0, 1);
                AddCard(StorageCard, 3, 0, 1);
                AddCard(DisplayCard, 4, 0, 1);
                AddCard(NetworkCard, 5, 0, 1);
                AddCard(AudioCard, 6, 0, 1);
                AddCard(InputCard, 7, 0, 1);
                AddCard(IntegrationCard, 8, 0, 1);
                AddCard(AdvancedCard, 9, 0, 1);
                break;
        }

        HardwareCardsGrid.DispatcherQueue.TryEnqueue(() => 页面过渡动画.布局稳定(
            PlatformCard,
            ProcessorCard,
            MemoryCard,
            StorageCard,
            DisplayCard,
            NetworkCard,
            AudioCard,
            InputCard,
            IntegrationCard,
            AdvancedCard));
        EditorScrollViewer.DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => EditorScrollViewer.ChangeView(horizontalOffset, verticalOffset, zoomFactor, true));
    }

    private void ConfigureGrid(int columnCount, int rowCount)
    {
        while (HardwareCardsGrid.ColumnDefinitions.Count > columnCount)
            HardwareCardsGrid.ColumnDefinitions.RemoveAt(HardwareCardsGrid.ColumnDefinitions.Count - 1);
        while (HardwareCardsGrid.ColumnDefinitions.Count < columnCount)
            HardwareCardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var definition in HardwareCardsGrid.ColumnDefinitions)
            definition.Width = new GridLength(1, GridUnitType.Star);

        while (HardwareCardsGrid.RowDefinitions.Count > rowCount)
            HardwareCardsGrid.RowDefinitions.RemoveAt(HardwareCardsGrid.RowDefinitions.Count - 1);
        while (HardwareCardsGrid.RowDefinitions.Count < rowCount)
            HardwareCardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var definition in HardwareCardsGrid.RowDefinitions)
            definition.Height = GridLength.Auto;
    }

    private void PrepareCardContainers(int layout)
    {
        if (layout != 1) return;
        HardwareCardsGrid.Children.Remove(peripheralLeftColumn);
        HardwareCardsGrid.Children.Remove(peripheralRightColumn);
    }

    private void AddCardColumn(
        StackPanel column,
        int row,
        int gridColumn,
        int columnSpan,
        params FrameworkElement[] cards)
    {
        foreach (var card in cards) MoveToPanel(card, column);
        AddCard(column, row, gridColumn, columnSpan);
    }

    private void AddCard(FrameworkElement card, int row, int column, int columnSpan)
    {
        card.HorizontalAlignment = HorizontalAlignment.Stretch;
        MoveToPanel(card, HardwareCardsGrid);
        PlaceCard(card, row, column, columnSpan);
    }

    private void MoveToPanel(FrameworkElement element, Panel destination)
    {
        if (destination.Children.Contains(element)) return;
        HardwareCardsGrid.Children.Remove(element);
        peripheralLeftColumn.Children.Remove(element);
        peripheralRightColumn.Children.Remove(element);
        destination.Children.Add(element);
    }

    private void ConfigureIdentity(bool compact)
    {
        if (compact)
        {
            ResizeIdentityGrid(2, 2);
            IdentityGrid.ColumnDefinitions[0].Width = new GridLength(26);
            IdentityGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetRow(IdentityIcon, 0);
            Grid.SetRowSpan(IdentityIcon, 2);
            Grid.SetColumn(NameField, 1);
            Grid.SetRow(NameField, 0);
            Grid.SetColumn(LocationField, 1);
            Grid.SetRow(LocationField, 1);
            return;
        }

        ResizeIdentityGrid(3, 1);
        IdentityGrid.ColumnDefinitions[0].Width = new GridLength(26);
        IdentityGrid.ColumnDefinitions[1].Width = new GridLength(0.85, GridUnitType.Star);
        IdentityGrid.ColumnDefinitions[2].Width = new GridLength(1.4, GridUnitType.Star);
        Grid.SetRow(IdentityIcon, 0);
        Grid.SetRowSpan(IdentityIcon, 1);
        Grid.SetColumn(NameField, 1);
        Grid.SetRow(NameField, 0);
        Grid.SetColumn(LocationField, 2);
        Grid.SetRow(LocationField, 0);
    }

    private void ResizeIdentityGrid(int columnCount, int rowCount)
    {
        while (IdentityGrid.ColumnDefinitions.Count > columnCount)
            IdentityGrid.ColumnDefinitions.RemoveAt(IdentityGrid.ColumnDefinitions.Count - 1);
        while (IdentityGrid.ColumnDefinitions.Count < columnCount)
            IdentityGrid.ColumnDefinitions.Add(new ColumnDefinition());
        while (IdentityGrid.RowDefinitions.Count > rowCount)
            IdentityGrid.RowDefinitions.RemoveAt(IdentityGrid.RowDefinitions.Count - 1);
        while (IdentityGrid.RowDefinitions.Count < rowCount)
            IdentityGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var definition in IdentityGrid.RowDefinitions)
            definition.Height = GridLength.Auto;
    }

    private static void PlaceCard(FrameworkElement card, int row, int column, int columnSpan)
    {
        Grid.SetRow(card, row);
        Grid.SetColumn(card, column);
        Grid.SetColumnSpan(card, columnSpan);
    }
}
