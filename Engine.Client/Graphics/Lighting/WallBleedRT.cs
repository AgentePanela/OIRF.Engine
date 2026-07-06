using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Ping-pong pair of render targets for the lightmap blur passes,
/// sized to match the lightmap.
/// </summary>
internal sealed class WallBleedRT
{
    private RenderTarget2D? _a;
    private RenderTarget2D? _b;
    private int _w, _h;

    public RenderTarget2D? A => _a;
    public RenderTarget2D? B => _b;

    public int Width => _w;
    public int Height => _h;

    public void EnsureSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        if (_a is not null && _b is not null && width == _w && height == _h)
            return;

        Dispose();

        _a = new RenderTarget2D(
            GameClient.GraphicsDevice, width, height,
            false, SurfaceFormat.Color, DepthFormat.None,
            0, RenderTargetUsage.PreserveContents);

        _b = new RenderTarget2D(
            GameClient.GraphicsDevice, width, height,
            false, SurfaceFormat.Color, DepthFormat.None,
            0, RenderTargetUsage.PreserveContents);

        _w = width;
        _h = height;
    }

    public void Dispose()
    {
        _a?.Dispose(); _a = null;
        _b?.Dispose(); _b = null;
        _w = 0; _h = 0;
    }
}
