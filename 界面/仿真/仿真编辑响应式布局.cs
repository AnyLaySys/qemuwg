using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace QemuWG.界面;

public sealed partial class 仿真编辑
{
    private int currentCardLayout;

    private void EditorScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
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
        currentCardLayout = layout;
        ConfigureIdentity(layout == 1);

        switch (layout)
        {
            case 3:
                ConfigureGrid(12, 5);
                PlaceCard(PlatformCard, 0, 0, 6);
                PlaceCard(ProcessorCard, 0, 6, 3);
                PlaceCard(MemoryCard, 0, 9, 3);
                PlaceCard(StorageCard, 1, 0, 6);
                PlaceCard(DisplayCard, 1, 6, 3);
                PlaceCard(NetworkCard, 1, 9, 3);
                PlaceCard(AudioCard, 2, 0, 6);
                PlaceCard(InputCard, 2, 6, 6);
                PlaceCard(IntegrationCard, 3, 0, 12);
                PlaceCard(AdvancedCard, 4, 0, 12);
                break;
            case 2:
                ConfigureGrid(2, 7);
                PlaceCard(PlatformCard, 0, 0, 2);
                PlaceCard(ProcessorCard, 1, 0, 1);
                PlaceCard(MemoryCard, 1, 1, 1);
                PlaceCard(StorageCard, 2, 0, 2);
                PlaceCard(DisplayCard, 3, 0, 1);
                PlaceCard(NetworkCard, 3, 1, 1);
                PlaceCard(AudioCard, 4, 0, 1);
                PlaceCard(InputCard, 4, 1, 1);
                PlaceCard(IntegrationCard, 5, 0, 2);
                PlaceCard(AdvancedCard, 6, 0, 2);
                break;
            default:
                ConfigureGrid(1, 10);
                PlaceCard(PlatformCard, 0, 0, 1);
                PlaceCard(ProcessorCard, 1, 0, 1);
                PlaceCard(MemoryCard, 2, 0, 1);
                PlaceCard(StorageCard, 3, 0, 1);
                PlaceCard(DisplayCard, 4, 0, 1);
                PlaceCard(NetworkCard, 5, 0, 1);
                PlaceCard(AudioCard, 6, 0, 1);
                PlaceCard(InputCard, 7, 0, 1);
                PlaceCard(IntegrationCard, 8, 0, 1);
                PlaceCard(AdvancedCard, 9, 0, 1);
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
