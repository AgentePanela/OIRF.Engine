using System;
using System.Collections.Generic;
using Engine.Client.UI;
using Microsoft.Xna.Framework;

namespace Engine.Client.Graphics;

/// <summary>
/// One (source, dest) rectangle pair of a 9-slice cut - draw it however fits the caller
/// (immediate ShapeBatch, queued RenderManager, ...).
/// </summary>
public readonly record struct NineSlicePatch(Rectangle Source, Rectangle Dest)
{
    /// <summary>
    /// Cuts a source region into 9 patches (source, dest) by margin.
    /// </summary>
    public static IEnumerable<NineSlicePatch> Compute(Rectangle source, Thickness margin, Rectangle dest)
    {
        var srcLeft = Math.Min(margin.Left, source.Width);
        var srcRight = Math.Min(margin.Right, source.Width - srcLeft);
        var srcTop = Math.Min(margin.Top, source.Height);
        var srcBottom = Math.Min(margin.Bottom, source.Height - srcTop);

        var destLeft = Math.Min(srcLeft, dest.Width);
        var destRight = Math.Min(srcRight, dest.Width - destLeft);
        var destTop = Math.Min(srcTop, dest.Height);
        var destBottom = Math.Min(srcBottom, dest.Height - destTop);

        var srcMidW = source.Width - srcLeft - srcRight;
        var srcMidH = source.Height - srcTop - srcBottom;
        var destMidW = dest.Width - destLeft - destRight;
        var destMidH = dest.Height - destTop - destBottom;

        // corners - native size
        yield return new NineSlicePatch(
            new Rectangle(source.X, source.Y, srcLeft, srcTop),
            new Rectangle(dest.X, dest.Y, destLeft, destTop));

        yield return new NineSlicePatch(
            new Rectangle(source.Right - srcRight, source.Y, srcRight, srcTop),
            new Rectangle(dest.Right - destRight, dest.Y, destRight, destTop));

        yield return new NineSlicePatch(
            new Rectangle(source.X, source.Bottom - srcBottom, srcLeft, srcBottom),
            new Rectangle(dest.X, dest.Bottom - destBottom, destLeft, destBottom));

        yield return new NineSlicePatch(
            new Rectangle(source.Right - srcRight, source.Bottom - srcBottom, srcRight, srcBottom),
            new Rectangle(dest.Right - destRight, dest.Bottom - destBottom, destRight, destBottom));

        // edges - stretched along
        yield return new NineSlicePatch(
            new Rectangle(source.X + srcLeft, source.Y, srcMidW, srcTop),
            new Rectangle(dest.X + destLeft, dest.Y, destMidW, destTop));

        yield return new NineSlicePatch(
            new Rectangle(source.X + srcLeft, source.Bottom - srcBottom, srcMidW, srcBottom),
            new Rectangle(dest.X + destLeft, dest.Bottom - destBottom, destMidW, destBottom));

        yield return new NineSlicePatch(
            new Rectangle(source.X, source.Y + srcTop, srcLeft, srcMidH),
            new Rectangle(dest.X, dest.Y + destTop, destLeft, destMidH));

        yield return new NineSlicePatch(
            new Rectangle(source.Right - srcRight, source.Y + srcTop, srcRight, srcMidH),
            new Rectangle(dest.Right - destRight, dest.Y + destTop, destRight, destMidH));

        // center - stretched on both axes
        yield return new NineSlicePatch(
            new Rectangle(source.X + srcLeft, source.Y + srcTop, srcMidW, srcMidH),
            new Rectangle(dest.X + destLeft, dest.Y + destTop, destMidW, destMidH));
    }
}
