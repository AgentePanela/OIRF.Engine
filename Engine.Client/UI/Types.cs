using Apos.Shapes;
using Engine.Shared.Prototypes;

namespace Engine.Client.UI;

/// <summary>
/// A box-model style margin/padding/etc... value.
/// </summary>
public struct Thickness
{
    [DataField("left")] public float Left;
    [DataField("top")] public float Top;
    [DataField("right")] public float Right;
    [DataField("bottom")] public float Bottom;

    // serialization req limitation
    public Thickness() : this(0)
    {
    }

    public Thickness(float all) : this(all, all, all, all)
    {
    }

    public Thickness(float horizontal, float vertical) : this(horizontal, vertical, horizontal, vertical)
    {
    }

    public Thickness(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public static explicit operator CornerRadii(Thickness t) => new(t.Left, t.Top, t.Right, t.Bottom);
    public static explicit operator Thickness(int v) => new(v);
}

public enum HorizontalAlignment
{
    Left,
    Center,
    Right,
    Stretch
}

public enum VerticalAlignment
{
    Top,
    Center,
    Bottom,
    Stretch
}

public enum Orientation
{
    Horizontal,
    Vertical,
    //Center
}

/// <summary>
/// Controls how a control participates in mouse hit-testing.
/// </summary>
public enum MouseFilterMode
{
    /// <summary>
    /// Not considered for hit-testing at all. Its children are
    /// still tested independently.
    /// </summary>
    Ignore,

    /// <summary>
    /// Receives mouse events. Nothing behind it (in the same branch) gets a chance to.
    /// </summary>
    Stop,

    /// <summary>
    /// Receives mouse events, same as Stop, but, let wharever 
    /// is behind it get the check too.
    /// </summary>
    Pass
}
