using Engine.Shared.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// When a render overlay will draw.
/// </summary>
public enum RenderStage
{
    /// <summary>
    /// Before anything in the world.
    /// </summary>
    BelowWorld,

    /// <summary>
    /// World space, with what the draw systems submit (sorted with it by layer and depth).
    /// </summary>
    World,

    /// <summary>
    /// World space after the world is rendered. 
    /// </summary>
    BeforeLighting,

    /// <summary>
    /// World space, after the lightnigng, everything here will not be affected by the light.
    /// </summary>
    AfterLighting,

    /// <summary>
    /// In pixels of the viewport's target. Post effects of the viewport run right
    /// after this.
    /// </summary>
    Screen,
}

/// <summary>
/// What a <see cref="RenderOverlay"/> is drawing for.
/// </summary>
public readonly struct RenderOverlayArgs
{
    public readonly IRenderManager Renderer;

    public readonly RenderViewport Viewport;
    public readonly RenderStage Stage;
    public readonly float DeltaTime;

    /// <summary>
    /// The frame as it was right before this overlay, for an overlay that asks for it with
    /// <see cref="RenderOverlay.RequestScreenTexture"/>
    /// </summary>
    public readonly RawTexture? ScreenTexture;

    public Camera2D Camera => Viewport.Camera;

    /// <summary>
    /// The world area the viewport shows.
    /// </summary>
    public RectangleF WorldBounds => Viewport.Camera.WorldBounds;

    /// <summary>
    /// The whole target, in the pixels the <see cref="RenderStage.Screen"/> stage draws in.
    /// </summary>
    public Rectangle ScreenBounds => new(0, 0, Viewport.TargetSize.X, Viewport.TargetSize.Y);

    public RenderOverlayArgs(IRenderManager renderer, RenderViewport viewport, RenderStage stage, float deltaTime,
        RawTexture? screenTexture)
    {
        Renderer = renderer;
        Viewport = viewport;
        Stage = stage;
        DeltaTime = deltaTime;
        ScreenTexture = screenTexture;
    }
}

public abstract class RenderOverlay
{
    public abstract RenderStage Stage { get; }

    /// <summary>
    /// Overlays of the same stage draw from the lowest to the highest.
    /// </summary>
    public virtual int Order => 0;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// True hands the overlay the frame drawn so far in <see cref="RenderOverlayArgs.ScreenTexture"/>, to draw it
    /// again changed.
    /// </summary>
    public virtual bool RequestScreenTexture => false;

    /// <summary>
    /// False skips a viewport. Every viewport by default, the main one and the others.
    /// </summary>
    public virtual bool AppliesTo(RenderViewport viewport) => true;

    public abstract void Draw(in RenderOverlayArgs args);
}
