using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using System;

namespace Engine.Client.Graphics;

public class Camera2D
{
    [Dependency] private readonly ViewportAdapter _viewport = default!;

    private float _zoom;
    private float _minimumZoom = 0.1f;
    private float _maximumZoom = float.MaxValue;
    private Vector2 _position;
    private float _rotation;

    public Vector2 WorldCenter => Position + Origin;

    public Vector2 ViewportCenter => new Vector2(ViewportMidX, ViewportMidY);
    public float ViewportMidX => (_viewport.VirtualWidth / 2f) / Zoom;
    public float ViewportMidY => (_viewport.VirtualHeight / 2f) / Zoom;

    public float ViewportLeft => WorldCenter.X - ViewportMidX;
    public float ViewportRight => WorldCenter.X + ViewportMidX;
    public float ViewportTop => WorldCenter.Y - ViewportMidY;
    public float ViewportBottom => WorldCenter.Y + ViewportMidY;

    public float ViewportWidth => GameClient.Options.Width / Zoom;
    public float ViewportHeight => GameClient.Options.Height / Zoom;

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
        float midX = (_viewport.VirtualWidth / 2f) / Zoom;
        float midY = (_viewport.VirtualHeight / 2f) / Zoom;
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
    /// Usually the center of the viewport resolution.
    /// </summary>
    public Vector2 Origin => new Vector2(GameClient.Options.Width / 2f, GameClient.Options.Height / 2f);

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
    /// This matrix does NOT include the viewport scaling (letterboxing).
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
    /// Gets the full view matrix, combining the camera transformations AND the viewport scaling.
    /// This is what RenderManager should use in SpriteBatch.Begin().
    /// </summary>
    public Matrix GetViewMatrix()
    {
        return GetVirtualViewMatrix() * _viewport.GetScaleMatrix();
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
        var pp = GameClient.GraphicsDevice.PresentationParameters;
        float offsetX = (pp.BackBufferWidth - _viewport.VirtualWidth) / 2f;
        float offsetY = (pp.BackBufferHeight - _viewport.VirtualHeight) / 2f;

        Vector2 adjusted = screenPosition - new Vector2(offsetX, offsetY);
        adjusted = Vector2.Transform(adjusted, Matrix.Invert(_viewport.GetScaleMatrix()));

        return Vector2.Transform(adjusted, Matrix.Invert(GetVirtualViewMatrix()));
    }

    /// <summary>
    /// Translates a World coordinate in the game to a screen coordinate.
    /// </summary>
    public Vector2 WorldToScreen(Vector2 worldPosition)
    {
        // Apply camera matrix
        Vector2 viewportPos = Vector2.Transform(worldPosition, GetVirtualViewMatrix());

        // Then apply physical screen letterboxing offsets
        var pp = GameClient.GraphicsDevice.PresentationParameters;
        float offsetX = (pp.BackBufferWidth - _viewport.VirtualWidth) / 2f;
        float offsetY = (pp.BackBufferHeight - _viewport.VirtualHeight) / 2f;

        return Vector2.Transform(viewportPos, _viewport.GetScaleMatrix()) + new Vector2(offsetX, offsetY);
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
