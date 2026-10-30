using Microsoft.Xna.Framework;

namespace Engine.Client.Graphics.Lighting;

internal sealed class OccluderDebugOverlay : RenderOverlay
{
    private static readonly Color CastColor = new(255, 220, 60);
    private static readonly Color SharedColor = new(120, 120, 120, 160);
    private static readonly Color InsideColor = new(255, 60, 60);

    private readonly OccluderSystem _occluders;

    public override RenderStage Stage => RenderStage.AfterLighting;

    public OccluderDebugOverlay(OccluderSystem occluders)
    {
        _occluders = occluders;
        Enabled = false;
    }

    public override void Draw(in RenderOverlayArgs args)
    {
        var renderer = args.Renderer;

        foreach (var (a, b, shared) in _occluders.EdgesIn(args.WorldBounds))
            renderer.DrawLine(a, b, shared ? 0.5f : 1f, shared ? SharedColor : CastColor);

        foreach (var light in _occluders.LightsInsideOccluders(args.WorldBounds))
            renderer.DrawCircle(light, 8f, borderColor: InsideColor, thickness: 2f);
    }
}
