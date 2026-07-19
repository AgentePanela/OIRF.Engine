using Apos.Shapes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// A debug/gizmo 3-point triangle queued to be drawn through
/// </summary>
public struct TriangleRenderable : IShapeRenderable
{
    public Vector2 A;
    public Vector2 B;
    public Vector2 C;
    public Gradient Fill;
    public Gradient Border;
    public float Thickness;
    public float Rounded;

    public int Layer { get; set; } = 9999;
    public float Depth { get; set; } = 0f;
    public SamplerState? SamplerState { get; set; } = null;
    public bool Unshaded { get; set; } = false;

    public TriangleRenderable()
    {
    }

    public void Draw(RenderManager renderer, Vector2 pos)
        => renderer.DrawTriangleShape(this);
}
