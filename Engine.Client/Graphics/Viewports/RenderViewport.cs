using System;
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
    public RenderTarget2D? ColorTarget { get; private set; }

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
        if (ColorTarget is { IsDisposed: false } target && target.Width == TargetSize.X && target.Height == TargetSize.Y)
            return target;

        ColorTarget?.Dispose();
        ColorTarget = new RenderTarget2D(device, TargetSize.X, TargetSize.Y, false, SurfaceFormat.Color, DepthFormat.None);
        return ColorTarget;
    }

    public void Dispose()
    {
        ColorTarget?.Dispose();
        ColorTarget = null;
    }
}
