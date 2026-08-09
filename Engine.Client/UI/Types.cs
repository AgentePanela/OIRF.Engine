namespace Engine.Client.UI;

/// <summary>
/// A box-model style margin/padding value — same shape as Myra's Thickness.
/// </summary>
public struct Thickness
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public Thickness(int all) : this(all, all, all, all)
    {
    }

    public Thickness(int horizontal, int vertical) : this(horizontal, vertical, horizontal, vertical)
    {
    }

    public Thickness(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }
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
