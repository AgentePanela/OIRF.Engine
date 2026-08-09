using Engine.Shared.Prototypes;
using Microsoft.Xna.Framework;
using AposGradient = Apos.Shapes.Gradient;

namespace Engine.Client.UI;

/// <summary>
/// Describes a 32-bit packed two color gradient.
/// </remarks>
public struct ColorGradient
{
    /// <summary>
    /// Left-to-right gradient across whatever shape this is drawn on.
    /// </summary>
    public static ColorGradient Horizontal(Color left, Color right)
        => new(new Vector2(0, 0), left, new Vector2(1, 0), right, Shape.Linear);

    /// <summary>
    /// Top-to-bottom gradient across whatever shape this is drawn on.
    /// </summary>
    public static ColorGradient Vertical(Color top, Color bottom)
        => new(new Vector2(0, 0), top, new Vector2(0, 1), bottom, Shape.Linear);

    /// <summary>
    /// Corner-to-corner (top-left to bottom-right) gradient across whatever shape this is drawn on.
    /// </summary>
    public static ColorGradient Diagonal(Color topLeft, Color bottomRight)
        => new(new Vector2(0, 0), topLeft, new Vector2(1, 1), bottomRight, Shape.Linear);

    /// <summary>
    /// Center-to-edge gradient across whatever shape this is drawn on.
    /// </summary>
    public static ColorGradient Radial(Color center, Color edge)
        => new(new Vector2(0.5f, 0.5f), center, new Vector2(1f, 0.5f), edge, Shape.Radial);

    public ColorGradient(Vector2 aXY, Color aC, Vector2 bXY, Color bC, Shape s = Shape.Linear, RepeatStyle rs = RepeatStyle.None, float aOffset = 0f, float bOffset = 0f)
    {
        AC = aC;
        AXY = aXY;
        AOffset = aOffset;
        BC = bC;
        BXY = bXY;
        BOffset = bOffset;
        S = s;
        RS = rs;
    }

    [DataField("pointA")] public Vector2 AXY;
    [DataField("colorA")] public Color AC;
    [DataField("offsetA")] public float AOffset;
    [DataField("pointB")] public Vector2 BXY;
    [DataField("colorB")] public Color BC;
    [DataField("offsetB")] public float BOffset;
    [DataField("shape")] public Shape S;
    [DataField("repeat")] public RepeatStyle RS;

    public enum Shape {
        None = 0,
        Radial = 1,
        Linear = 2,
        Bilinear = 3,
        Conical = 4,
        ConicalAsym = 5,
        Square = 6,
        Cross = 7,
        SpiralCW = 8,
        SpiralCCW = 9,
    }
    public enum RepeatStyle {
        None = 0,
        Sawtooth = 1,
        Triangle = 2,
        Sine = 3,
    }

    /// <summary>
    /// A solid color constructor.
    /// </summary>
    public ColorGradient(Color color) : this(Vector2.Zero, color, Vector2.Zero, color, Shape.None)
    {
    }

    // mirror XNA Color constructor

    public ColorGradient(int r, int g, int b) : this(new Color(r, g, b))
    {
    }

    public ColorGradient(int r, int g, int b, int a) : this(new Color(r, g, b, a))
    {
    }

    public ColorGradient(float r, float g, float b) : this(new Color(r, g, b))
    {
    }

    public ColorGradient(float r, float g, float b, float a) : this(new Color(r, g, b, a))
    {
    }

    public static implicit operator ColorGradient(Color c) => new(c);

    /// <summary>
    /// Turns the fractional AXY/BXY into real pixel coordinates within <paramref name="bounds"/>,
    /// producing the actual Apos.Shapes.Gradient to hand to a draw call.
    /// </summary>
    public AposGradient Resolve(Rectangle bounds)
    {
        var a = new Vector2(bounds.X + AXY.X * bounds.Width, bounds.Y + AXY.Y * bounds.Height);
        var b = new Vector2(bounds.X + BXY.X * bounds.Width, bounds.Y + BXY.Y * bounds.Height);

        var result = new AposGradient(a, AC, b, BC);
        result.AOffset = AOffset;
        result.BOffset = BOffset;
        result.S = (AposGradient.Shape)(int)S;
        result.RS = (AposGradient.RepeatStyle)(int)RS;
        result.IsLocal = false;
        return result;
    }
}
