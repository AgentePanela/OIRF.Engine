using Apos.Shapes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.UI;

public enum TextureSampling
{
    /// <summary>
    /// Smooth - default.
    /// </summary>
    Linear,

    /// <summary>
    /// Nearest pixel, for pixel art.
    /// </summary>
    Point,
}

/// <summary>
/// Starts the UI ShapeBatch with the state the control being drawn needs. Anything that has to End() the batch mid-draw
/// (to change the scissor rect, for example) must Begin() it again through here, or it drops the current sampling.
/// </summary>
public static class UIBatch
{
    private static readonly RasterizerState ScissorRasterizer = new() { ScissorTestEnable = true };
    public static TextureSampling Sampling { get; private set; } = TextureSampling.Linear;

    public static void Begin(ShapeBatch sb, float uiScale)
    {
        var sampler = Sampling == TextureSampling.Point ? SamplerState.PointClamp : null;
        sb.Begin(view: Matrix.CreateScale(uiScale), samplerState: sampler, rasterizerState: ScissorRasterizer);
    }
    
    public static void SetSampling(ShapeBatch sb, float uiScale, TextureSampling sampling)
    {
        if (sampling == Sampling)
            return;

        sb.End();
        Sampling = sampling;
        Begin(sb, uiScale);
    }

    internal static void Reset() => Sampling = TextureSampling.Linear;
}
