using System;
using System.Collections.Generic;
using System.Diagnostics;
using Apos.Shapes;
using Engine.Client.Graphics.Fonts;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.UI;

public abstract partial class Control : IDisposable
{
    private long _lastProfileStart;
    // ShapeBatch queues draws and only actually submits them at End() - so heres a little hack
    private static readonly RasterizerState ScissorRasterizer = new() { ScissorTestEnable = true };

    /// <summary>
    /// Whether this control clips its children to its own <see cref="Bounds"/>. 
    /// </summary>
    protected internal virtual bool ClipsContent => false;

    /// <summary>
    /// Draws this control and its subtree, clipped to <see cref="Bounds"/> intersected with
    /// whatever was already clipped by an ancestor.
    /// </summary>
    internal void Draw(ShapeBatch sb, IFontManager fontManager, float dt)
    {
        if (!EffectivelyVisible)
            return;

        var device = GameClient.GraphicsDevice;
        var previousScissor = device.ScissorRectangle;
        var clipped = Rectangle.Intersect(previousScissor, Bounds);

        if (clipped.Width <= 0 || clipped.Height <= 0)
            return; // fully clipped out - this and everything under it is off-screen

        _lastProfileStart = Stopwatch.GetTimestamp();
        DrawSelf(sb, fontManager, dt);
        UIProfiler.Record(GetType().Name, Stopwatch.GetTimestamp() - _lastProfileStart);

        if (Children.Count == 0)
            return;

        var scissorChanged = ClipsContent && clipped != previousScissor;
        if (scissorChanged)
        {
            sb.End(); //todo: fork apos.shapes and add Flush as public member instead of end/begin
            device.ScissorRectangle = clipped;
            sb.Begin(rasterizerState: ScissorRasterizer);
        }

        var ordered = OrderedChildren;
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].Draw(sb, fontManager, dt);

        if (scissorChanged)
        {
            sb.End();
            device.ScissorRectangle = previousScissor;
            sb.Begin(rasterizerState: ScissorRasterizer);
        }
    }

    /// <summary>
    /// Draws this control's own visuals (background, text, sprite, etc... not its children).
    /// Runs before children, so children render on top. The base implementation draws nothing.
    /// </summary>
    protected virtual void DrawSelf(ShapeBatch sb, IFontManager fontManager, float dt)
    {
    }
}
