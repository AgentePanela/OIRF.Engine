using Apos.Shapes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// A hexagon shape queued to be drawn through
/// </summary>
public struct HexagonRenderable : IShapeRenderable
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

    public HexagonRenderable()
    {
    }

    public void Draw(RenderManager renderer, Vector2 pos)
        => renderer.DrawHexagonShape(this);
}
