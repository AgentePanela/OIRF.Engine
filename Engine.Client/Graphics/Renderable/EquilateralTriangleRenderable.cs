using Apos.Shapes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// A equilateral triangle shape queued to be drawn through
/// </summary>
public struct EquilateralTriangleRenderable : IShapeRenderable
{
    public Vector2 Center;
    public float Radius;
    public Gradient Fill;
    public Gradient Border;
    public float Thickness;
    public float Rounded;
    public float Rotation;

    public int Layer { get; set; } = 9999;
    public float Depth { get; set; } = 0f;
    public SamplerState? SamplerState { get; set; } = null;
    public bool Unshaded { get; set; } = false;

    public EquilateralTriangleRenderable()
    {
    }

    public void Draw(RenderManager renderer, Vector2 pos)
        => renderer.DrawEquilateralTriangleShape(this);
}
