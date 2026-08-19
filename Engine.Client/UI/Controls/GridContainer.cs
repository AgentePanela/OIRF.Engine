using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

/// <summary>
/// Lays children out in a uniform grid with a fixed number of columns, wrapping to a new row
/// once a row fills up.
/// </summary>
public partial class GridContainer : PanelContainer
{
    [StyleField("columns", 1)]
    private int? _columns;

    /// <summary>
    /// Gap in pixels between columns.
    /// </summary>
    [StyleField("hSeparation", 0f)]
    private float? _hSeparation;

    /// <summary>
    /// Gap in pixels between rows.
    /// </summary>
    [StyleField("vSeparation", 0f)]
    private float? _vSeparation;

    private float[] _columnWidths = System.Array.Empty<float>();
    private float[] _rowHeights = System.Array.Empty<float>();

    protected override Vector2 MeasureCore(Vector2 availableSize)
    {
        // keep in sync with DrawSelf bounds - see ScrollContainer for why
        var overflow = OutlineOverflow;
        availableSize = new Vector2(
            MathHelper.Max(0, availableSize.X - overflow.Left - overflow.Right),
            MathHelper.Max(0, availableSize.Y - overflow.Top - overflow.Bottom));

        var columns = MathHelper.Max(1, Columns);
        var rows = Children.Count == 0 ? 0 : (Children.Count + columns - 1) / columns;

        _columnWidths = new float[columns];
        _rowHeights = new float[rows];

        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(availableSize);

            var col = i % columns;
            var row = i / columns;

            _columnWidths[col] = MathHelper.Max(_columnWidths[col], child.DesiredSize.X);
            _rowHeights[row] = MathHelper.Max(_rowHeights[row], child.DesiredSize.Y);
        }

        var totalWidth = Sum(_columnWidths) + (columns > 1 ? HSeparation * (columns - 1) : 0f);
        var totalHeight = Sum(_rowHeights) + (rows > 1 ? VSeparation * (rows - 1) : 0f);

        return new Vector2(totalWidth, totalHeight) + new Vector2(overflow.Left + overflow.Right, overflow.Top + overflow.Bottom);
    }

    protected override void ArrangeCore(Rectangle finalRect)
    {
        finalRect = PanelRect(finalRect);

        if (Children.Count == 0)
            return;

        var columns = MathHelper.Max(1, Columns);

        var columnX = new float[columns];
        var offset = (float)finalRect.X;
        for (var c = 0; c < columns; c++)
        {
            columnX[c] = offset;
            offset += (c < _columnWidths.Length ? _columnWidths[c] : 0f) + HSeparation;
        }

        var rowY = finalRect.Y;
        var row = 0;

        for (var i = 0; i < Children.Count; i++)
        {
            var col = i % columns;

            if (col == 0 && i > 0)
            {
                rowY += (int)_rowHeights[row] + (int)VSeparation;
                row++;
            }

            var cellRect = new Rectangle((int)columnX[col], rowY, (int)_columnWidths[col], (int)_rowHeights[row]);
            Children[i].Arrange(cellRect);
        }
    }

    private static float Sum(float[] values)
    {
        var total = 0f;
        foreach (var v in values)
            total += v;

        return total;
    }
}
