using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace QemuWG.界面;

public sealed partial class 虚拟机编辑
{
    private int currentCardLayout;

    private void HardwareCardsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var availableWidth = XamlRoot?.Size.Width ?? e.NewSize.Width;
        var availableHeight = XamlRoot?.Size.Height ?? EditorContentFrame.ActualHeight;
        var horizontalPadding = Math.Clamp(availableWidth * 0.012, 8, 20);
        var verticalPadding = Math.Clamp(availableHeight * 0.014, 10, 20);
        EditorContentFrame.Padding = new Thickness(horizontalPadding, verticalPadding, horizontalPadding, verticalPadding);

        var layout = e.NewSize.Width switch
        {
            >= 1100 => 3,
            >= 700 => 2,
            _ => 1
        };
        if (layout == currentCardLayout) return;
        currentCardLayout = layout;

        switch (layout)
        {
            case 3:
                ConfigureGrid(12, 4);
                PlaceCard(PlatformCard, 0, 0, 6);
                PlaceCard(ProcessorCard, 0, 6, 3);
                PlaceCard(MemoryCard, 0, 9, 3);
                PlaceCard(StorageCard, 1, 0, 6);
                PlaceCard(DisplayCard, 1, 6, 3);
                PlaceCard(NetworkCard, 1, 9, 3);
                PlaceCard(AudioCard, 2, 0, 6);
                PlaceCard(InputCard, 2, 6, 6);
                PlaceCard(AdvancedCard, 3, 0, 12);
                break;
            case 2:
                ConfigureGrid(2, 6);
                PlaceCard(PlatformCard, 0, 0, 2);
                PlaceCard(ProcessorCard, 1, 0, 1);
                PlaceCard(MemoryCard, 1, 1, 1);
                PlaceCard(StorageCard, 2, 0, 2);
                PlaceCard(DisplayCard, 3, 0, 1);
                PlaceCard(NetworkCard, 3, 1, 1);
                PlaceCard(AudioCard, 4, 0, 1);
                PlaceCard(InputCard, 4, 1, 1);
                PlaceCard(AdvancedCard, 5, 0, 2);
                break;
            default:
                ConfigureGrid(1, 9);
                PlaceCard(PlatformCard, 0, 0, 1);
                PlaceCard(ProcessorCard, 1, 0, 1);
                PlaceCard(MemoryCard, 2, 0, 1);
                PlaceCard(StorageCard, 3, 0, 1);
                PlaceCard(DisplayCard, 4, 0, 1);
                PlaceCard(NetworkCard, 5, 0, 1);
                PlaceCard(AudioCard, 6, 0, 1);
                PlaceCard(InputCard, 7, 0, 1);
                PlaceCard(AdvancedCard, 8, 0, 1);
                break;
        }
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

    private static void PlaceCard(FrameworkElement card, int row, int column, int columnSpan)
    {
        Grid.SetRow(card, row);
        Grid.SetColumn(card, column);
        Grid.SetColumnSpan(card, columnSpan);
    }
}
