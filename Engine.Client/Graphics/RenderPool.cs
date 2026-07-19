using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/*
this is a hack for the object boxing that was occouring in the RenderManager Submit call. 
This has showed to improve the render manager in ~0.5ms/1ms
if u know any better way to do this, please, do.
*/

/// <summary>
/// Non-generic interface so DrawQueue can return pooled entries without knowing the class.
/// </summary>
internal interface IPooledRenderable
{
    void ReturnToPool();
}

/// <summary>
/// Reusable class wrapper that holds a struct IRenderable inline.
/// </summary>
internal sealed class Boxed<T> : IRenderable, IPooledRenderable where T : struct, IRenderable
{
    public T Value;

    public int Layer
    {
        get => Value.Layer;
        set => Value.Layer = value;
    }

    public float Depth
    {
        get => Value.Depth;
        set => Value.Depth = value;
    }

    public SamplerState? SamplerState => Value.SamplerState;

    public bool UsesShapeBatch => Value.UsesShapeBatch;

    public bool Unshaded => Value.Unshaded;

    public void Draw(RenderManager renderer, Microsoft.Xna.Framework.Vector2 pos)
        => Value.Draw(renderer, pos);

    public void ReturnToPool()
        => RenderPool<T>.Return(this);
}

/// <summary>
/// Per type static pool of boxed instances
/// Each unique T gets its own pool via generic type specialization.
/// </summary>
internal static class RenderPool<T> where T : struct, IRenderable
{
    private static readonly Stack<Boxed<T>> Pool = new();

    public static Boxed<T> Rent()
    {
        return Pool.Count > 0 ? Pool.Pop() : new Boxed<T>();
    }

    public static void Return(Boxed<T> box)
    {
        Pool.Push(box);
    }
}
