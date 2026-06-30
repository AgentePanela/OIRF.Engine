using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// Reprensents a class that can be queue to be rendered in <code>RenderQueue</code>
/// </summary>
public interface IRenderable
{
    public int Layer {get; set;}
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
    
    public RenderQueue(IRenderable target, Vector2 pos, Effect? shader = null)
    {
        Target = target;
        Position = pos;
        Shader = shader;
    }
}
