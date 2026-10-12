using System.Collections.Generic;

namespace Engine.Client.Graphics;

/// <summary>
/// What the renderer did in one frame.
/// </summary>
public sealed class RenderStats
{
    public readonly record struct ViewportStat(string Name, int Width, int Height, int Items, double Ms);

    /// <summary>
    /// Entries queued to be drawn.
    /// </summary>
    public int Submitted;

    /// <summary>
    /// What draw systems skipped for being off screen.
    /// </summary>
    public int Culled;

    public int SpriteBatches;
    public int ShapeBatches;

    // why a batch had to end and another begin
    public int BreaksByTexture;
    public int BreaksByShader;
    public int BreaksBySampler;
    public int BreaksByBlend;
    public int BreaksByKind;

    /// <summary>
    /// Sprites, glyphs and tiles sent through the sprite batch.
    /// </summary>
    public int Quads;

    public int SharedShaders;
    public int OwnedShaders;

    // from the graphics device, for the world only (the UI draws after)
    public int DrawCalls;
    public int Primitives;
    public int TextureSwaps;
    public int TargetSwitches;

    public double SubmitMs;
    public double SortMs;
    public double FlushMs;
    public double PresentMs;
    public double TotalMs;

    public readonly List<ViewportStat> Viewports = new();

    public int Batches => SpriteBatches + ShapeBatches;

    internal void Reset()
    {
        Submitted = Culled = 0;
        SpriteBatches = ShapeBatches = 0;
        BreaksByTexture = BreaksByShader = BreaksBySampler = BreaksByBlend = BreaksByKind = 0;
        Quads = SharedShaders = OwnedShaders = 0;
        DrawCalls = Primitives = TextureSwaps = TargetSwitches = 0;
        SubmitMs = SortMs = FlushMs = PresentMs = TotalMs = 0;
        Viewports.Clear();
    }

    internal void CopyFrom(RenderStats other)
    {
        Submitted = other.Submitted;
        Culled = other.Culled;
        SpriteBatches = other.SpriteBatches;
        ShapeBatches = other.ShapeBatches;
        BreaksByTexture = other.BreaksByTexture;
        BreaksByShader = other.BreaksByShader;
        BreaksBySampler = other.BreaksBySampler;
        BreaksByBlend = other.BreaksByBlend;
        BreaksByKind = other.BreaksByKind;
        Quads = other.Quads;
        SharedShaders = other.SharedShaders;
        OwnedShaders = other.OwnedShaders;
        DrawCalls = other.DrawCalls;
        Primitives = other.Primitives;
        TextureSwaps = other.TextureSwaps;
        TargetSwitches = other.TargetSwitches;
        SubmitMs = other.SubmitMs;
        SortMs = other.SortMs;
        FlushMs = other.FlushMs;
        PresentMs = other.PresentMs;
        TotalMs = other.TotalMs;

        Viewports.Clear();
        Viewports.AddRange(other.Viewports);
    }
}
