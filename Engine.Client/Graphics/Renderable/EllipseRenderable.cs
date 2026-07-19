using Apos.Shapes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// A ellipse shape queued to be drawn through=
/// </summary>
public struct EllipseRenderable : IRenderable
{
    public Vector2 Center;
    public float RadiusX;
    public float RadiusY;
    public Gradient Fill;
    public Gradient Border;
    public float Thickness;
    public float Rotation;

    public int Layer { get; set; } = 9999;
    public float Depth { get; set; } = 0f;
    public SamplerState? SamplerState { get; set; } = null;
    public bool UsesShapeBatch => true;
    public bool Unshaded { get; set; } = false;

    public EllipseRenderable()
    {
    }

    public void Draw(RenderManager renderer, Vector2 pos)
        => renderer.DrawEllipseShape(this);
}
