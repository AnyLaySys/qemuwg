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
        currentCardLayout = layout;
        ConfigureIdentity(layout == 1);
        DetachCardsFromCurrentLayout();

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
        HardwareCardsGrid.ColumnDefinitions.Clear();
        for (var index = 0; index < columnCount; index++)
            HardwareCardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        HardwareCardsGrid.RowDefinitions.Clear();
        for (var index = 0; index < rowCount; index++)
            HardwareCardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    }

    private void DetachCardsFromCurrentLayout()
    {
        peripheralLeftColumn.Children.Clear();
        peripheralRightColumn.Children.Clear();
        HardwareCardsGrid.Children.Clear();
    }

    private void AddCardColumn(
        StackPanel column,
        int row,
        int gridColumn,
        int columnSpan,
        params FrameworkElement[] cards)
    {
        foreach (var card in cards) column.Children.Add(card);
        AddCard(column, row, gridColumn, columnSpan);
    }

    private void AddCard(FrameworkElement card, int row, int column, int columnSpan)
    {
        card.HorizontalAlignment = HorizontalAlignment.Stretch;
        HardwareCardsGrid.Children.Add(card);
        PlaceCard(card, row, column, columnSpan);
    }

    private void ConfigureIdentity(bool compact)
    {
        IdentityGrid.ColumnDefinitions.Clear();
        IdentityGrid.RowDefinitions.Clear();

        IdentityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        if (compact)
        {
            IdentityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            IdentityGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            IdentityGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(IdentityIcon, 0);
            Grid.SetRowSpan(IdentityIcon, 2);
            Grid.SetColumn(NameField, 1);
            Grid.SetRow(NameField, 0);
            Grid.SetColumn(LocationField, 1);
            Grid.SetRow(LocationField, 1);
            return;
        }

        IdentityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });
        IdentityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        IdentityGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(IdentityIcon, 0);
        Grid.SetRowSpan(IdentityIcon, 1);
        Grid.SetColumn(NameField, 1);
        Grid.SetRow(NameField, 0);
        Grid.SetColumn(LocationField, 2);
        Grid.SetRow(LocationField, 0);
    }

    private static void PlaceCard(FrameworkElement card, int row, int column, int columnSpan)
    {
        Grid.SetRow(card, row);
        Grid.SetColumn(card, column);
        Grid.SetColumnSpan(card, columnSpan);
    }
}
