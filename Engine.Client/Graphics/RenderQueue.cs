using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// Reprensents a class that can be queue to be rendered in <code>RenderQueue</code>
/// </summary>
public interface IRenderable
{
    public int Layer {get; set;}

    /// <summary>
    /// Sort key inside the same Layer, higher draws later (on top). Basically Construct's per-layer Z-order.
    /// </summary>
    public float Depth { get; set; }
    public SamplerState? SamplerState { get; }

    /// <summary>
    /// True when this renderable draws through <see cref="GameClient.ShapeBatch"/>
    /// (Apos.Shapes) instead of <see cref="GameClient.SpriteBatch"/>.
    /// </summary>
    public bool UsesShapeBatch => false;

    /// <summary>
    /// True to opt this renderable out of the lighting multiply — it's diverted
    /// to the unshaded queue and drawn at full brightness on top of the composed
    /// lightmap. Equivalent to a sprite's Effect carrying the "Unshaded"
    /// technique, but independent of Effect so shapes (which carry no Effect)
    /// can opt in too.
    /// </summary>
    public bool Unshaded => false;

    void Draw(RenderManager renderer, Vector2 pos);
}

/// <summary>
/// Represents a <code>IRenderable</code> to be queued in RenderManager.
/// </summary>
public struct RenderQueue
{
    public IRenderable Target { get; }
    public Vector2 Position { get; }
    public Effect? Shader { get; }
    public int SubmitOrder { get; set; }

    public RenderQueue(IRenderable target, Vector2 pos, Effect? shader = null)
    {
        Target = target;
        Position = pos;
        Shader = shader;
        SubmitOrder = 0;
    }
}
