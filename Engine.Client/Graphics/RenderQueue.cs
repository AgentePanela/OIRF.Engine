using System;
using System.Collections.Generic;
using Engine.Client.Graphics.Shaders;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// Something that draws itself when its turn in the render queue comes.
/// </summary>
public interface IRenderable
{
    public int Layer {get; set;}

    /// <summary>
    /// Sort key inside the same Layer, higher draws later (on top). Basically Construct's per-layer Z-order.
    /// </summary>
    public float Depth { get; set; }
    public SamplerState? SamplerState { get; }

    void Draw(IRenderManager renderer, Vector2 pos);
}

/// <summary>
/// Represents a <code>IRenderable</code> to be queued in RenderManager.
/// </summary>
public readonly struct RenderQueue
{
    public IRenderable Target { get; }
    public Vector2 Position { get; }
    public ShaderInstance? Shader { get; }

    public RenderQueue(IRenderable target, Vector2 pos, ShaderInstance? shader = null)
    {
        Target = target;
        Position = pos;
        Shader = shader;
    }
}

internal enum DrawCommandType : byte
{
    /// <summary>
    /// An Sprite2D from the Textures/ folder.
    /// </summary>
    Sprite,

    /// <summary>
    /// An <see cref="IRenderable"/> that draws itself into the sprite batch.
    /// </summary>
    Renderable,

    /// <summary>
    /// An <see cref="IShapeRenderable"/>, drawn through the shape batch.
    /// </summary>
    Shape,
}

internal struct DrawCommand
{
    public DrawCommandType Kind;

    /// <summary>
    /// False for what ignores the lighting.
    /// </summary>
    // todo: light rework
    public bool Lit;

    public ShaderInstance? Shader;
    public SamplerState? Sampler;
    public Vector2 Position;

    // Sprite
    public Texture2D Texture;
    public Rectangle Source;
    public Color Color;
    public float Rotation;
    public Vector2 Origin;
    public Vector2 Scale;
    public SpriteEffects Effects;
    public Vector4 Custom0;
    public Vector4 Custom1;

    // Renderable and Shape
    public IRenderable? Target;
}

/// <summary>
/// What the queue is sorted by.
/// </summary>
internal readonly struct DrawSortEntry : IComparable<DrawSortEntry>
{
    // layer in the high half, depth in the low one
    public readonly ulong Key;

    // Where the command is, which is also the order it was submitted in: entries with the same layer and depth
    // keep it.
    public readonly int Index;

    public DrawSortEntry(int layer, float depth, int index)
    {
        // flipping the sign bit makes ints and floats sort right as unsigned
        var layerBits = (uint)layer ^ 0x80000000u;
        var depthBits = BitConverter.SingleToUInt32Bits(depth);
        depthBits = (depthBits & 0x80000000u) != 0 ? ~depthBits : depthBits | 0x80000000u;

        Key = ((ulong)layerBits << 32) | depthBits;
        Index = index;
    }

    public int CompareTo(DrawSortEntry other)
    {
        var cmp = Key.CompareTo(other.Key);
        return cmp != 0 ? cmp : Index.CompareTo(other.Index);
    }
}

/// <summary>
/// The commands waiting to be drawn, with the pooled boxes they hold.
/// </summary>
internal sealed class DrawCommandBuffer
{
    public DrawCommand[] Commands = new DrawCommand[1024];
    public DrawSortEntry[] Order = new DrawSortEntry[1024];
    public int Count;

    public readonly List<IPooledRenderable> Pooled = new();

    public ref DrawCommand Add(int layer, float depth)
    {
        if (Count == Commands.Length)
        {
            Array.Resize(ref Commands, Count * 2);
            Array.Resize(ref Order, Count * 2);
        }

        Order[Count] = new DrawSortEntry(layer, depth, Count);
        return ref Commands[Count++];
    }

    public void Sort()
        => Order.AsSpan(0, Count).Sort();

    public void Clear()
    {
        Array.Clear(Commands, 0, Count);
        Count = 0;

        foreach (var pooled in Pooled)
            pooled.ReturnToPool();
        Pooled.Clear();
    }
}
