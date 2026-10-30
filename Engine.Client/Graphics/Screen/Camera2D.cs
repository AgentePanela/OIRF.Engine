using Engine.Shared.Common;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using System;

namespace Engine.Client.Graphics;

public class Camera2D
{
    /// <summary>
    /// The viewport this camera looks through.
    /// </summary>
    public RenderViewport? Viewport { get; internal set; }

    // the part of the world one screenful covers at zoom 1
    private Vector2 ViewSize => Viewport?.VisibleSize ?? new Vector2(GameClient.Options.Width, GameClient.Options.Height);

    private float _zoom;
    private float _minimumZoom = 0.1f;
    private float _maximumZoom = float.MaxValue;
    private Vector2 _position;
    private float _rotation;

    public Vector2 WorldCenter => Position + Origin;

    public Vector2 ViewportCenter => new Vector2(ViewportMidX, ViewportMidY);
    public float ViewportMidX => (ViewSize.X / 2f) / Zoom;
    public float ViewportMidY => (ViewSize.Y / 2f) / Zoom;

    public float ViewportLeft => WorldCenter.X - ViewportMidX;
    public float ViewportRight => WorldCenter.X + ViewportMidX;
    public float ViewportTop => WorldCenter.Y - ViewportMidY;
    public float ViewportBottom => WorldCenter.Y + ViewportMidY;

    public float ViewportWidth => ViewSize.X / Zoom;
    public float ViewportHeight => ViewSize.Y / Zoom;

    public Rectangle ViewportBounds => new Rectangle(
        (int)ViewportLeft,
        (int)ViewportTop,
        (int)ViewportWidth,
        (int)ViewportHeight);

    public Camera2D()
    {
        IoCManager.ResolveDependencies(this);
        
        _zoom = 1f;
        _rotation = 0f;
        _position = Vector2.Zero;
    }

    // cached viewport bounds
    private float _vpLeft, _vpRight, _vpTop, _vpBottom;

    /// <summary>
    /// Caches viewport bounds for the current frame.
    /// </summary>
    internal void CacheFrame()
    {
        var center = Position + Origin;
        float midX = ViewportMidX;
        float midY = ViewportMidY;

        // a rotated view covers a bigger axis aligned box
        if (Rotation != 0f)
        {
            float cos = MathF.Abs(MathF.Cos(Rotation));
            float sin = MathF.Abs(MathF.Sin(Rotation));
            (midX, midY) = (midX * cos + midY * sin, midX * sin + midY * cos);
        }

        _vpLeft = center.X - midX;
        _vpRight = center.X + midX;
        _vpTop = center.Y - midY;
        _vpBottom = center.Y + midY;
    }

    /// <summary>
    /// Gets or sets the world position of the camera.
    /// </summary>
    public Vector2 Position
    {
        get => _position;
        set => _position = value;
    }

    /// <summary>
    /// Gets or sets the rotation of the camera in radians.
    /// </summary>
    public float Rotation
    {
        get => _rotation;
        set => _rotation = value;
    }

    /// <summary>
    /// Gets or sets the zoom level of the camera.
    /// 1.0f is default scale.
    /// </summary>
    public float Zoom
    {
        get => _zoom;
        set => _zoom = MathHelper.Clamp(value, _minimumZoom, _maximumZoom);
    }

    /// <summary>
    /// Gets or sets the minimum allowed zoom level.
    /// </summary>
    public float MinimumZoom
    {
        get => _minimumZoom;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "MinimumZoom must be greater than zero");
            _minimumZoom = value;
            _zoom = MathHelper.Clamp(_zoom, _minimumZoom, _maximumZoom);
        }
    }

    /// <summary>
    /// Gets or sets the maximum allowed zoom level.
    /// </summary>
    public float MaximumZoom
    {
        get => _maximumZoom;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "MaximumZoom must be greater than zero");
            _maximumZoom = value;
            _zoom = MathHelper.Clamp(_zoom, _minimumZoom, _maximumZoom);
        }
    }

    /// <summary>
    /// Gets the origin point for rotation and zoom transformations.
    /// The center of the viewport this camera looks through.
    /// </summary>
    public Vector2 Origin => Viewport is { } viewport
        ? viewport.Size / 2f
        : new Vector2(GameClient.Options.Width / 2f, GameClient.Options.Height / 2f);

    /// <summary>
    /// The world area the last <see cref="CacheFrame"/> saw, what the IsOnScreen checks test against.
    /// </summary>
    public RectangleF WorldBounds => new(_vpLeft, _vpTop, _vpRight - _vpLeft, _vpBottom - _vpTop);

    /// <summary>
    /// Moves the camera by the given delta vector, taking rotation into account.
    /// </summary>
    public void Move(Vector2 direction)
    {
        Position += Vector2.Transform(direction, Matrix.CreateRotationZ(-Rotation));
    }

    /// <summary>
    /// Adjusts the camera zoom.
    /// </summary>
    public void ZoomIn(float deltaZoom) => Zoom += deltaZoom;
    public void ZoomOut(float deltaZoom) => Zoom -= deltaZoom;

    /// <summary>
    /// Centers the camera exactly on the specified world position.
    /// </summary>
    public void LookAt(Vector2 position)
    {
        Position = position - Origin;
    }

    /// <summary>
    /// Calculates the view matrix to be used in SpriteBatch.Begin().
    /// This matrix does NOT include the viewport render scale.
    /// </summary>
    public Matrix GetVirtualViewMatrix()
    {
        return Matrix.CreateTranslation(new Vector3(-Position, 0.0f)) *
               Matrix.CreateTranslation(new Vector3(-Origin, 0.0f)) *
               Matrix.CreateRotationZ(Rotation) *
               Matrix.CreateScale(Zoom, Zoom, 1) *
               Matrix.CreateTranslation(new Vector3(Origin, 0.0f));
    }

    /// <summary>
    /// Gets the full view matrix: world to pixels of the viewport's render target.
    /// </summary>
    public Matrix GetViewMatrix()
    {
        return Viewport is { } viewport ? viewport.GetViewMatrix() : GetVirtualViewMatrix();
    }

    /// <summary>
    /// Gets the inverse of the full view matrix.
    /// </summary>
    public Matrix GetInverseViewMatrix()
    {
        return Matrix.Invert(GetViewMatrix());
    }

    /// <summary>
    /// Translates a screen coordinate (e.g. Mouse Position) to a World coordinate in the game.
    /// </summary>
    public Vector2 ScreenToWorld(Vector2 screenPosition)
    {
        var offset = Viewport?.ScreenOffset ?? Vector2.Zero;
        return Vector2.Transform(screenPosition - offset, GetInverseViewMatrix());
    }

    /// <summary>
    /// Translates a World coordinate in the game to a screen coordinate.
    /// </summary>
    public Vector2 WorldToScreen(Vector2 worldPosition)
    {
        var offset = Viewport?.ScreenOffset ?? Vector2.Zero;
        return Vector2.Transform(worldPosition, GetViewMatrix()) + offset;
    }

    public void Reset()
    {
        _zoom = 1f;
        _position = Vector2.Zero;
        _rotation = 0f;
    }

    public bool IsOnScreen(Vector2 pos)
    {
        return pos.X >= _vpLeft &&
            pos.X <= _vpRight &&
            pos.Y >= _vpTop &&
            pos.Y <= _vpBottom;
    }

    public bool IsOnScreen(Vector2 position, Vector2 size)
    {
        return position.X < _vpRight &&
            position.X + size.X > _vpLeft &&
            position.Y < _vpBottom &&
            position.Y + size.Y > _vpTop;
    }

    public bool IsOnScreen(Vector2 position, Vector2 size, Vector2 origin, Vector2 scale)
    {
        Vector2 scaledSize = size * scale;

        Vector2 topLeft = position - (origin * scale);

        return topLeft.X < _vpRight &&
            topLeft.X + scaledSize.X > _vpLeft &&
            topLeft.Y < _vpBottom &&
            topLeft.Y + scaledSize.Y > _vpTop;
    }

    public bool IsOnScreen(Sprite2D sprite, Vector2 position)
    {
        if (sprite.Rotation == 0f)
        {
            Vector2 scaledSize = new Vector2(sprite.Width, sprite.Height) * sprite.Scale;
            Vector2 topLeft = position - sprite.Origin * sprite.Scale;

            return topLeft.X < _vpRight &&
                topLeft.X + scaledSize.X > _vpLeft &&
                topLeft.Y < _vpBottom &&
                topLeft.Y + scaledSize.Y > _vpTop;
        }

        Vector2 p0 = TransformCorner(new Vector2(0, 0), sprite, position);
        Vector2 p1 = TransformCorner(new Vector2(sprite.Width, 0), sprite, position);
        Vector2 p2 = TransformCorner(new Vector2(0, sprite.Height), sprite, position);
        Vector2 p3 = TransformCorner(new Vector2(sprite.Width, sprite.Height), sprite, position);

        float minX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        float maxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        float minY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        float maxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));

        return maxX > _vpLeft &&
            minX < _vpRight &&
            maxY > _vpTop &&
            minY < _vpBottom;
    }

    private static Vector2 TransformCorner(Vector2 corner, Sprite2D sprite, Vector2 position)
    {
        Vector2 local = corner - sprite.Origin;
        local *= sprite.Scale;
        if (sprite.Rotation != 0f)
            local = Vector2.Transform(local, Matrix.CreateRotationZ(sprite.Rotation));

        return local + position;
    }
}
