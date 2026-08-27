using System;
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
public struct RenderQueue : IComparable<RenderQueue>
{
    public IRenderable Target { get; }
    public Vector2 Position { get; }
    public Effect? Shader { get; }
    public int SubmitOrder { get; set; }

    /// <summary>
    /// True when this entry should bypass lighting (full brightness). Resolved once
    /// at submit time from the shader's technique name
    /// </summary>
    public bool Unshaded { get; }

    public RenderQueue(IRenderable target, Vector2 pos, Effect? shader = null, bool unshaded = false)
    {
        Target = target;
        Position = pos;
        Shader = shader;
        SubmitOrder = 0;
        Unshaded = unshaded;
    }

    public int CompareTo(RenderQueue other)
    {
        var layerCmp = Target.Layer.CompareTo(other.Target.Layer);
        if (layerCmp != 0)
            return layerCmp;

        var depthCmp = Target.Depth.CompareTo(other.Target.Depth);
        if (depthCmp != 0)
            return depthCmp;

        var unshadedCmp = Unshaded.CompareTo(other.Unshaded);
        return unshadedCmp != 0 ? unshadedCmp : SubmitOrder.CompareTo(other.SubmitOrder);
    }
}
