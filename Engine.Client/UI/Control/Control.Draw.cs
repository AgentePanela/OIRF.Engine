using System;
using System.Collections.Generic;
using Apos.Shapes;
using Engine.Client.Graphics.Fonts;
using Microsoft.Xna.Framework;

namespace Engine.Client.UI;

public abstract partial class Control : IDisposable
{
    /// <summary>
    /// Draws this control and its subtree, clipped to <see cref="Bounds"/> intersected with
    /// whatever was already clipped by an ancestor.
    /// </summary>
    internal void Draw(ShapeBatch sb, IFontManager fontManager)
    {
        if (!EffectivelyVisible)
            return;

        var device = GameClient.GraphicsDevice;
        var previousScissor = device.ScissorRectangle;
        var clipped = Rectangle.Intersect(previousScissor, Bounds);

        if (clipped.Width <= 0 || clipped.Height <= 0)
            return; // fully clipped out - this and everything under it is off-screen

        device.ScissorRectangle = clipped;

        DrawSelf(sb, fontManager);

        foreach (var child in Children)
            child.Draw(sb, fontManager);

        device.ScissorRectangle = previousScissor;
    }

    /// <summary>
    /// Draws this control's own visuals (background, text, sprite, etc... not its children).
    /// Runs before children, so children render on top. The base implementation draws nothing.
    /// </summary>
    protected virtual void DrawSelf(ShapeBatch sb, IFontManager fontManager)
    {
    }
}