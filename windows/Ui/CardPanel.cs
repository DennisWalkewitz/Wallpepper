using System.Windows;
using System.Windows.Controls;

namespace Wallpepper.Ui;

/// <summary>Responsive grid: as many equal columns as fit, cards stretch to fill the row, 16:9 thumbnail plus a label strip.</summary>
internal sealed class CardPanel : Panel
{
    public double MinCardWidth { get; set; } = 196;

    public double Gap { get; set; } = 16;

    public double LabelHeight { get; set; } = 38;

    public int Columns { get; private set; } = 1;

    protected override Size MeasureOverride(Size available)
    {
        Size card = CardSize(available.Width);
        int count = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(card);
            count++;
        }
        int rows = (count + Columns - 1) / Columns;
        double width = double.IsInfinity(available.Width) ? card.Width * Columns + Gap * (Columns - 1) : available.Width;
        return new Size(width, rows == 0 ? 0 : rows * card.Height + (rows - 1) * Gap);
    }

    protected override Size ArrangeOverride(Size final)
    {
        Size card = CardSize(final.Width);
        int index = 0;
        foreach (UIElement child in InternalChildren)
        {
            int column = index % Columns;
            int row = index / Columns;
            child.Arrange(new Rect(column * (card.Width + Gap), row * (card.Height + Gap), card.Width, card.Height));
            index++;
        }
        return final;
    }

    private Size CardSize(double width)
    {
        if (double.IsInfinity(width) || width <= 0)
        {
            width = MinCardWidth;
        }
        Columns = Math.Max(1, (int)Math.Floor((width + Gap) / (MinCardWidth + Gap)));
        double cardWidth = Math.Floor((width - Gap * (Columns - 1)) / Columns);
        return new Size(cardWidth, Math.Round(cardWidth * 9 / 16) + LabelHeight);
    }
}
