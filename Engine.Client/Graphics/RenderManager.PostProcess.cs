using Engine.Client.Graphics.Lighting;
using Engine.Client.Graphics.Shaders;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// Post-processing extension of <see cref="RenderManager"/>. Provides:
/// 1. Render-target swap so the scene can be drawn to an offscreen buffer.
/// 2. A fullscreen effect submission that samples the offscreen scene and
///    applies a shader to produce the final frame.
///
/// This is the integration point used by the lighting system (Fase 2).
/// </summary>
public sealed partial class RenderManager
{
    /// <summary>
    /// The offscreen target where the world is drawn. Allocated lazily
    /// based on the virtual viewport size and resized on demand.
    /// </summary>
    public RenderTarget2D? SceneTarget { get; private set; }

    /// <summary>
    /// Viewport that was active on the backbuffer the last time DrawQueue
    /// ran. The lighting apply pass uses this to keep the letterboxed
    /// aspect ratio intact when blitting the SceneTarget to the screen.
    /// </summary>
    public Viewport LastBackbufferViewport { get; internal set; }

    private RenderTarget2D? _previousTarget;
    private Viewport _previousViewport;
    private bool _inSceneRender;

    /// <summary>
    /// Ensures <see cref="SceneTarget"/> exists and matches the requested
    /// size. Cheap to call every frame — only reallocates on resize.
    /// </summary>
    public void EnsureSceneTarget(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        if (SceneTarget is not null &&
            SceneTarget.Width == width &&
            SceneTarget.Height == height)
            return;

        SceneTarget?.Dispose();

        var pp = GameClient.GraphicsDevice.PresentationParameters;
        SceneTarget = new RenderTarget2D(
            GameClient.GraphicsDevice,
            width,
            height,
            false,
            SurfaceFormat.Color,
            DepthFormat.None,
            0,
            RenderTargetUsage.PreserveContents);

        Log.Debug($"RenderManager.SceneTarget resized to {width}x{height}");
    }

    /// <summary>
    /// Switch the active render target to <see cref="SceneTarget"/>
    /// (or any custom RT). Restores the previous target (backbuffer) when
    /// <see cref="EndSceneRender"/> is called.
    /// </summary>
    public void BeginSceneRender(RenderTarget2D? target = null)
    {
        if (_inSceneRender)
            return;
        _inSceneRender = true;

        // Cache the current target + viewport so EndSceneRender can restore
        // them exactly. BeginSceneRender is only used by the lighting
        // lightmap pass (offscreen target), so we can safely set null
        // current target here.
        _previousViewport = GameClient.GraphicsDevice.Viewport;
        _previousTarget = null;

        var dest = target ?? SceneTarget;
        if (dest is null)
            return;

        GameClient.GraphicsDevice.SetRenderTarget(dest);
        GameClient.GraphicsDevice.Viewport = new Viewport(0, 0, dest.Width, dest.Height);
    }

    /// <summary>
    /// Restore the render target that was active before
    /// <see cref="BeginSceneRender"/> (backbuffer).
    /// </summary>
    public void EndSceneRender()
    {
        if (!_inSceneRender)
            return;
        GameClient.GraphicsDevice.SetRenderTarget(_previousTarget);
        GameClient.GraphicsDevice.Viewport = _previousViewport;
        _previousTarget = null;
        _inSceneRender = false;
    }

    /// <summary>
    /// Submits a fullscreen quad that samples <paramref name="sceneTexture"/>
    /// (and optionally <paramref name="lightmap"/>) and applies the given effect.
    /// The effect must expose <c>SceneTexture</c> and optionally <c>LightMap</c> parameters.
    ///
    /// Called automatically by <see cref="LightingSystem"/>.
    /// </summary>
    public void SubmitFullscreenEffectWithTextures(Effect effect, Texture2D sceneTexture, Texture2D? lightmap)
    {
        if (effect is null)
            return;

        if (effect.Parameters["SceneTexture"] is not null)
            effect.Parameters["SceneTexture"].SetValue(sceneTexture);

        if (lightmap is not null && effect.Parameters["LightMap"] is not null)
            effect.Parameters["LightMap"].SetValue(lightmap);

        // Always use Identity transform matrix here — the apply pass is a
        // screen-space quad and the camera transform would distort the
        // result. The caller is expected to have set the backbuffer as the
        // active render target already.
        GameClient.SpriteBatch.Begin(
            samplerState: SamplerState.PointClamp,
            blendState: BlendState.Opaque,
            transformMatrix: Matrix.Identity,
            effect: effect);

        GameClient.SpriteBatch.Draw(
            sceneTexture,
            new Rectangle(0, 0,
                GameClient.GraphicsDevice.Viewport.Width,
                GameClient.GraphicsDevice.Viewport.Height),
            Color.White);

        GameClient.SpriteBatch.End();
    }
}
