using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

/// <summary>
/// A thin invisible line, fixed thickness on one axis and stretched on the other.
/// </summary>
public sealed partial class Separator : PanelContainer
{
    private Orientation _orientation = Orientation.Horizontal;

    public Orientation Orientation
    {
        get => _orientation;
        set => SetLayoutField(ref _orientation, value);
    }

    [StyleField("thickness", 2f)]
    private float? _thickness;

    public Separator()
    {
        StyleAliasses.Add("separator");
    }

    public Separator(Orientation orientation) : this()
    {
        Orientation = orientation;
    }

    protected override Vector2 MeasureCore(Vector2 availableSize)
        => Orientation == Orientation.Horizontal ? new Vector2(0f, Thickness) : new Vector2(Thickness, 0f);
}
