using Apos.Shapes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// A line shape queued to be drawn through
/// </summary>
public struct LineRenderable : IRenderable
{
    public Vector2 A;
    public Vector2 B;
    public float Radius;
    public Gradient Fill;
    public Gradient Border;
    public float Thickness;

    public int Layer { get; set; } = 9999;
    public float Depth { get; set; } = 0f;
    public SamplerState? SamplerState { get; set; } = null;
    public bool UsesShapeBatch => true;
    public bool Unshaded { get; set; } = false;

    public LineRenderable()
    {
    }

    public void Draw(RenderManager renderer, Vector2 pos)
        => renderer.DrawLineShape(this);
}
