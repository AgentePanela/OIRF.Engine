using Apos.Shapes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// A shape ring queued to be drawn through <see cref="GameClient.ShapeBatch"/>
/// </summary>
public struct RingRenderable : IShapeRenderable
{
    public Vector2 Center;
    public float Angle1;
    public float Angle2;
    public float Radius1;
    public float Radius2;
    public Gradient Fill;
    public Gradient Border;
    public float Thickness;

    public int Layer { get; set; } = 9999;
    public float Depth { get; set; } = 0f;
    public SamplerState? SamplerState { get; set; } = null;
    public bool Unshaded { get; set; } = false;

    public RingRenderable()
    {
    }

    public void Draw(RenderManager renderer, Vector2 pos)
        => renderer.DrawRingShape(this);
}
