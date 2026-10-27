using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Engine.Client.Graphics.Shaders;
using Engine.Shared.Common;
using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// A view into the world with its own camera, drawn into its own render target.
/// </summary>
public sealed class RenderViewport : IDisposable
{
    /// <summary>
    /// For profiller/debbuging.
    /// </summary>
    public string Name { get; }

    public Camera2D Camera { get; }

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The size the camera centers on, in world units at zoom 1.
    /// </summary>
    public Vector2 Size { get; private set; }

    /// <summary>
    /// How many target pixels one world unit takes at zoom 1.
    /// </summary>
    public float RenderScale { get; private set; } = 1f;

    /// <summary>
    /// The render target size in pixels. When it is not <see cref="Size"/> times <see cref="RenderScale"/> the view
    /// stays centered: a smaller target crops the edges, a bigger one shows more of the world.
    /// </summary>
    public Point TargetSize { get; private set; }

    /// <summary>
    /// What the target is cleared to, the scene background when null.
    /// </summary>
    public Color? ClearColor { get; set; }

    /// <summary>
    /// Where the top left of the target sits on the window.
    /// </summary>
    public Vector2 ScreenOffset { get; set; }

    /// <summary>
    /// The last frame drawn for this viewport, null until it is drawn once.
    /// </summary>
    public RenderTarget2D? Target { get; private set; }

    /// <summary>
    /// The last frame drawn, ready to be submitted: its top left sits on the position it is submitted at. Null until
    /// the viewport is drawn once.
    /// </summary>
    public RawTexture? Frame => Target is { } target ? new RawTexture(target) { Origin = Vector2.Zero } : null;

    /// <summary>
    /// Shaders the finished frame is drawn through, one after the other, after the Screen stage. Any shader works:
    /// the frame is a sprite as big as the target, so LocalUV runs 0..1 over the screen.
    /// </summary>
    public List<ShaderInstance> PostEffects { get; } = new();

    public bool LightingEnabled { get; set; } = true;

    /// <summary>
    /// The light of the last frame.
    /// </summary>
    public RenderTarget2D? LightTarget { get; private set; }

    public RectangleF LightMapBounds { get; internal set; }

    /// <summary>
    /// The pixels of <see cref="LightTarget"/> that hold <see cref="LightMapBounds"/>.
    /// </summary>
    public Point LightMapSize { get; internal set; }

    public Vector4 LightMapTransform { get; internal set; }

    /// <summary>
    /// Whether the last frame was drawn lit.
    /// </summary>
    [MemberNotNullWhen(true, nameof(LightTarget))]
    public bool WasLit { get; internal set; }

    /// <summary>
    /// The light of the last frame.
    /// </summary>
    public RawTexture? LightFrame => WasLit && LightTarget is { } target
        ? new RawTexture(target)
        {
            Region = new Rectangle(0, 0, LightMapSize.X, LightMapSize.Y),
            Origin = Vector2.Zero,
            Scale = new Vector2(LightMapBounds.Width / LightMapSize.X, LightMapBounds.Height / LightMapSize.Y),
        }
        : null;

    // The other target of the same size: reading the frame while drawing needs one to read and one to draw into.
    private RenderTarget2D? _scratch;

    /// <summary>
    /// The entity the viewport sees from
    /// </summary>
    public EntityUid? Eye { get; set; }

    /// <summary>
    /// Where the eye sits, from the position of <see cref="Eye"/>.
    /// </summary>
    public Vector2 EyeOffset { get; set; }

    public bool FovEnabled { get; set; } = true;

    /// <summary>
    /// How far the eye sees, 0 no limit.
    /// </summary>
    public float FovRadius { get; set; }

    /// <summary>
    /// How wide the edge of <see cref="FovRadius"/> fades.
    /// </summary>
    public float FovRadiusSoftness { get; set; } = 32f;

    /// <summary>
    /// What the eye did not see last frame, in the alpha (1 is hidden). Null until there is an eye. 
    /// </summary>
    public RenderTarget2D? FovTarget { get; private set; }

    public RectangleF FovMaskBounds { get; internal set; }

    /// <summary>
    /// The pixels of <see cref="FovTarget"/> that hold <see cref="FovMaskBounds"/>.
    /// </summary>
    public Point FovMaskSize { get; internal set; }

    public Vector4 FovMaskTransform { get; internal set; }

    /// <summary>
    /// Whether the last frame was drawn with a field of view.
    /// </summary>
    [MemberNotNullWhen(true, nameof(FovTarget))]
    public bool HadFov { get; internal set; }

    internal Vector2 FovEyePosition;

    /// <summary>
    /// What the eye did not see last frame (alpha 1 is hidden), ready to be submitted over the world where it was.
    /// </summary>
    public RawTexture? FovMask => HadFov && FovTarget is { } target
        ? new RawTexture(target)
        {
            Region = new Rectangle(0, 0, FovMaskSize.X, FovMaskSize.Y),
            Origin = Vector2.Zero,
            Scale = new Vector2(FovMaskBounds.Width / FovMaskSize.X, FovMaskBounds.Height / FovMaskSize.Y),
        }
        : null;

    // todo: multi z
    // public int Z { get; set; }

    /// <summary>
    /// The size of the world area the target shows at zoom 1.
    /// </summary>
    public Vector2 VisibleSize => new(TargetSize.X / RenderScale, TargetSize.Y / RenderScale);

    internal RenderViewport(string name, Camera2D camera)
    {
        Name = name;
        Camera = camera;
        camera.Viewport = this;
    }

    /// <summary>
    /// The target is recreated on the next draw when its pixel size changed.
    /// </summary>
    /// <param name="targetSize">Size * scale when null.</param>
    public void Resize(Vector2 size, float renderScale = 1f, Point? targetSize = null)
    {
        Size = size;
        RenderScale = MathF.Max(renderScale, 0.0001f);

        var target = targetSize ?? new Point((int)MathF.Round(size.X * RenderScale), (int)MathF.Round(size.Y * RenderScale));
        TargetSize = new Point(Math.Max(1, target.X), Math.Max(1, target.Y));
    }

    /// <summary>
    /// World to pixels of the render target.
    /// </summary>
    public Matrix GetViewMatrix()
    {
        // whole pixels, a half pixel shift blurs everything
        var offsetX = MathF.Floor((TargetSize.X - Size.X * RenderScale) / 2f);
        var offsetY = MathF.Floor((TargetSize.Y - Size.Y * RenderScale) / 2f);

        return Camera.GetVirtualViewMatrix()
               * Matrix.CreateScale(RenderScale, RenderScale, 1f)
               * Matrix.CreateTranslation(offsetX, offsetY, 0f);
    }

    internal RenderTarget2D EnsureTarget(GraphicsDevice device)
    {
        if (Target is { IsDisposed: false } target && target.Width == TargetSize.X && target.Height == TargetSize.Y)
            return target;

        Target?.Dispose();
        Target = new RenderTarget2D(device, TargetSize.X, TargetSize.Y, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
        return Target;
    }

    /// <summary>
    /// Makes the other target the one being drawn into and returns the one that held the frame, which can now be
    /// read.
    /// </summary>
    internal RenderTarget2D SwapTargets(GraphicsDevice device)
    {
        var frame = EnsureTarget(device);

        if (_scratch is not { IsDisposed: false } scratch || scratch.Width != frame.Width || scratch.Height != frame.Height)
        {
            _scratch?.Dispose();
            scratch = new RenderTarget2D(device, frame.Width, frame.Height, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
        }

        Target = scratch;
        _scratch = frame;
        return frame;
    }

    // the light target grows in steps of this, so zooming does not make a new one every frame
    private const int LightTargetStep = 64;

    internal RenderTarget2D EnsureLightTarget(GraphicsDevice device, Point size)
    {
        if (LightTarget is { IsDisposed: false } target
            && target.Width >= size.X && target.Height >= size.Y
            && target.Width <= size.X * 2 + LightTargetStep && target.Height <= size.Y * 2 + LightTargetStep)
            return target;

        LightTarget?.Dispose();
        LightTarget = new RenderTarget2D(device, RoundUp(size.X), RoundUp(size.Y), false, SurfaceFormat.Color,
            DepthFormat.None);
        return LightTarget;

        static int RoundUp(int value) => (value + LightTargetStep - 1) / LightTargetStep * LightTargetStep;
    }

    /// <summary>
    /// The video memory its targets take.
    /// </summary>
    internal RenderTarget2D EnsureFovTarget(GraphicsDevice device, Point size)
    {
        if (FovTarget is { IsDisposed: false } target
            && target.Width >= size.X && target.Height >= size.Y
            && target.Width <= size.X * 2 + LightTargetStep && target.Height <= size.Y * 2 + LightTargetStep)
            return target;

        FovTarget?.Dispose();
        FovTarget = new RenderTarget2D(device, RoundUp(size.X), RoundUp(size.Y), false, SurfaceFormat.Color,
            DepthFormat.None);
        return FovTarget;

        static int RoundUp(int value) => (value + LightTargetStep - 1) / LightTargetStep * LightTargetStep;
    }

    internal long TargetBytes
        => TargetSize.X * (long)TargetSize.Y * 8 * ((Target is null ? 0 : 1) + (_scratch is null ? 0 : 1))
           + (LightTarget is { } light ? light.Width * (long)light.Height * 4 : 0)
           + (FovTarget is { } fov ? fov.Width * (long)fov.Height * 4 : 0);

    public void Dispose()
    {
        Target?.Dispose();
        Target = null;
        _scratch?.Dispose();
        _scratch = null;
        LightTarget?.Dispose();
        LightTarget = null;
        WasLit = false;
        FovTarget?.Dispose();
        FovTarget = null;
        HadFov = false;
    }
}
