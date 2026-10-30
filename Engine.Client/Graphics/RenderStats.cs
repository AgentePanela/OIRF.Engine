using System.Collections.Generic;
using Engine.Client.Graphics.Lighting;

namespace Engine.Client.Graphics;

/// <summary>
/// What the renderer did in one frame.
/// </summary>
public sealed class RenderStats
{
    public readonly record struct ViewportStat(string Name, int Width, int Height, int Items, double Ms);

    public readonly record struct OverlayStat(string Name, RenderStage Stage, double Ms);

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
    public int BreaksByMask;
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

    public int PostEffects;
    public double PostProcessMs;

    /// <summary>
    /// What the lighting did, see the LIGHTING panel of the profiler.
    /// </summary>
    public readonly LightStats Light = new();

    /// <summary>
    /// Video memory taken by the targets of the viewports drawn.
    /// </summary>
    public long TargetBytes;

    public readonly List<ViewportStat> Viewports = new();
    public readonly List<OverlayStat> Overlays = new();

    public int Batches => SpriteBatches + ShapeBatches;

    internal void Reset()
    {
        Submitted = Culled = 0;
        SpriteBatches = ShapeBatches = 0;
        BreaksByTexture = BreaksByShader = BreaksBySampler = BreaksByBlend = BreaksByMask = BreaksByKind = 0;
        Quads = SharedShaders = OwnedShaders = 0;
        DrawCalls = Primitives = TextureSwaps = TargetSwitches = 0;
        SubmitMs = SortMs = FlushMs = PresentMs = TotalMs = 0;
        PostEffects = 0;
        PostProcessMs = 0;
        Light.Reset();
        TargetBytes = 0;
        Viewports.Clear();
        Overlays.Clear();
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
        BreaksByMask = other.BreaksByMask;
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

        PostEffects = other.PostEffects;
        PostProcessMs = other.PostProcessMs;
        Light.CopyFrom(other.Light);
        TargetBytes = other.TargetBytes;

        Viewports.Clear();
        Viewports.AddRange(other.Viewports);
        Overlays.Clear();
        Overlays.AddRange(other.Overlays);
    }
}
