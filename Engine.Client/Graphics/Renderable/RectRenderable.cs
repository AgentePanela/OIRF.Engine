using Apos.Shapes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// A rectangle shape queued to be drawn through
/// </summary>
public struct RectRenderable : IShapeRenderable
{
    public Vector2 XY;
    public Vector2 Size;
    public Gradient Fill;
    public Gradient Border;
    public float Thickness;
    public CornerRadii Rounded;
    public float Rotation;

    public int Layer { get; set; } = 9999;
    public float Depth { get; set; } = 0f;
    public SamplerState? SamplerState { get; set; } = null;
    public bool Unshaded { get; set; } = false;

    public RectRenderable()
    {
    }

    public void Draw(RenderManager renderer, Vector2 pos)
        => renderer.DrawRectShape(this);
}
