using Engine.Client.Graphics.Shaders;
using Engine.Shared.Common;
using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;

namespace Engine.Client.Graphics;

/// <summary>
/// The mask of one viewport being built, cleared to all seen.
/// </summary>
public readonly struct FovMaskContext
{
    private readonly RenderManager _renderer;

    public readonly RenderViewport Viewport;
    public readonly EntityUid Eye;

    public readonly Vector2 EyePosition;

    /// <summary>
    /// The world area the mask covers
    /// </summary>
    public readonly RectangleF Bounds;

    /// <summary>
    /// What is submitted through it in <see cref="FovRenderer.BuildMask"/>, in world positions, is drawn into the
    /// mask after the shadows, replacing what is there (transparent pixels too).
    /// </summary>
    public IRenderManager Renderer => _renderer;

    internal FovMaskContext(RenderManager renderer, RenderViewport viewport, EntityUid eye, Vector2 eyePosition,
        RectangleF bounds)
    {
        _renderer = renderer;
        Viewport = viewport;
        Eye = eye;
        EyePosition = eyePosition;
        Bounds = bounds;
    }

    /// <summary>
    /// Hides what the occluders hide from <paramref name="from"/>, out to <paramref name="radius"/>
    /// </summary>
    public void DrawShadows(Vector2 from, float radius = 0f)
        => _renderer.DrawFovShadows(from, radius);
}

public readonly struct FovComposeContext
{
    private readonly RenderManager _renderer;

    public readonly RenderViewport Viewport;

    public readonly Vector4 Color;

    /// <summary>
    /// What <see cref="ShadowFov"/> covers with: fov.color where hidden.
    /// </summary>
    public readonly ShaderInstance? DefaultShader;

    internal FovComposeContext(RenderManager renderer, RenderViewport viewport, Vector4 color,
        ShaderInstance? defaultShader)
    {
        _renderer = renderer;
        Viewport = viewport;
        Color = color;
        DefaultShader = defaultShader;
    }

    /// <summary>
    /// Draws <paramref name="shader"/> over the whole target, blended over the frame.
    /// </summary>
    public void DrawOver(ShaderInstance shader)
        => _renderer.DrawFovQuad(Viewport, shader, filter: false);

    /// <summary>
    /// Draws the whole frame again through <paramref name="shader"/>, which gets it as SpriteTexture and replaces it.
    /// </summary>
    public void Filter(ShaderInstance shader)
        => _renderer.DrawFovQuad(Viewport, shader, filter: true);
}
