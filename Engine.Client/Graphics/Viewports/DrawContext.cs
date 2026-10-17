using Engine.Shared.Common;

namespace Engine.Client.Graphics;

/// <summary>
/// What a draw system is drawing for - Per viewport.
/// </summary>
public readonly struct DrawContext
{
    public readonly float DeltaTime;
    public readonly RenderViewport Viewport;

    /// <summary>
    /// The world area the viewport shows.
    /// </summary>
    public readonly RectangleF WorldBounds;

    // todo: multi z
    // public readonly int Z;

    public Camera2D Camera => Viewport.Camera;

    public DrawContext(float deltaTime, RenderViewport viewport, RectangleF worldBounds)
    {
        DeltaTime = deltaTime;
        Viewport = viewport;
        WorldBounds = worldBounds;
    }
}
