using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Scratch target for the lightmap blur ping-pong. Allocated at half the
/// lightmap size - the horizontal pass writes here, the vertical one reads
/// it back into the full-res lightmap.
/// </summary>
internal sealed class ScratchRT
{
    private RenderTarget2D? _target;
    private int _w, _h;

    public RenderTarget2D? Target => _target;

    public void EnsureSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        if (_target is not null && width == _w && height == _h)
            return;

        Dispose();

        _target = new RenderTarget2D(
            GameClient.GraphicsDevice, width, height,
            false, SurfaceFormat.Color, DepthFormat.None,
            0, RenderTargetUsage.DiscardContents);

        _w = width;
        _h = height;
    }

    public void Dispose()
    {
        _target?.Dispose(); _target = null;
        _w = 0; _h = 0;
    }
}
