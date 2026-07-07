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
