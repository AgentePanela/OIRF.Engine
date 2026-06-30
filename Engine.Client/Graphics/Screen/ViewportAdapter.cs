using Engine.Shared.Configuration;
using Engine.Shared.Configuration.CVars;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Engine.Client.Graphics;

public class ViewportAdapter
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    public int VirtualWidth { get; private set; }
    public int VirtualHeight { get; private set; }

    // The scale matrix applied to the sprite batch to make the virtual resolution fit the screen
    private Matrix _scaleMatrix = Matrix.Identity;
    private bool scaleOuter = true;

    internal void Init()
    {
        IoCManager.ResolveDependencies(this);
        VirtualWidth = GameClient.Options.Width;
        VirtualHeight = GameClient.Options.Height;
        _cfg.Subs(GameCVars.ScaleOuter, v => 
        {
            scaleOuter = v;
            if (GameClient.GraphicsDevice is not null)
                UpdateScaleMatrix();
        });
    }

    /// <summary>
    /// Recalculates the scale matrix to fit the virtual resolution inside the physical window (letterboxing/pillarboxing).
    /// </summary>
    public void UpdateScaleMatrix()
    {
        var pp = GameClient.GraphicsDevice.PresentationParameters;
        var ops = GameClient.Options;

        int screenWidth = pp.BackBufferWidth;
        int screenHeight = pp.BackBufferHeight;

        if (screenWidth <= 0 || screenHeight <= 0)
            return;

        float scaleX = screenWidth / (float)ops.Width;
        float scaleY = screenHeight / (float)ops.Height;

        float scale = 0f;
        if (scaleOuter)
            scale = Math.Max(scaleX, scaleY);
        else
            scale = Math.Min(scaleX, scaleY);

        VirtualWidth = (int)Math.Round(ops.Width * scale);
        VirtualHeight = (int)Math.Round(ops.Height * scale);

        _scaleMatrix = Matrix.CreateScale(scale, scale, 1f);

        int viewportX = (screenWidth - VirtualWidth) / 2;
        int viewportY = (screenHeight - VirtualHeight) / 2;

        GameClient.GraphicsDevice.Viewport = new Viewport(
            viewportX,
            viewportY,
            VirtualWidth,
            VirtualHeight
        );
    }

    public Matrix GetScaleMatrix() => _scaleMatrix;

    /// <summary>
    /// Translates a physical screen coordinate to the virtual resolution coordinate.
    /// This DOES NOT account for the camera position, only the window scaling.
    /// </summary>
    public Vector2 PointToScreen(Vector2 screenPosition)
    {
        var pp = GameClient.GraphicsDevice.PresentationParameters;
        float offsetX = (pp.BackBufferWidth - VirtualWidth) / 2f;
        float offsetY = (pp.BackBufferHeight - VirtualHeight) / 2f;

        Vector2 adjusted = new Vector2(
            screenPosition.X - offsetX,
            screenPosition.Y - offsetY
        );

        return Vector2.Transform(adjusted, Matrix.Invert(_scaleMatrix));
    }
}