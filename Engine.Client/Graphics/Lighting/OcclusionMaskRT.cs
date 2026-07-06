using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Single-channel mask of "which pixels are reached by a shadow-casting
/// light", consumed by the wall bleed merge pass. Alpha8 when the device
/// supports it, Color otherwise.
/// </summary>
internal sealed class OcclusionMaskRT
{
    private RenderTarget2D? _target;
    private int _allocatedWidth;
    private int _allocatedHeight;

    public RenderTarget2D? Target => _target;

    public int Width => _allocatedWidth;
    public int Height => _allocatedHeight;

    public bool Usable { get; private set; }

    /// <summary>
    /// Cheap to call every frame, only reallocates when the size changes.
    /// </summary>
    public void EnsureSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        if (_target is not null && width == _allocatedWidth && height == _allocatedHeight)
            return;

        Dispose();

        var device = GameClient.GraphicsDevice;

        try
        {
            _target = new RenderTarget2D(
                device,
                width,
                height,
                false,
                SurfaceFormat.Alpha8,
                DepthFormat.None,
                0,
                RenderTargetUsage.DiscardContents);
            Usable = true;
        }
        catch (System.Exception)
        {
            try
            {
                _target = new RenderTarget2D(
                    device,
                    width,
                    height,
                    false,
                    SurfaceFormat.Color,
                    DepthFormat.None,
                    0,
                    RenderTargetUsage.DiscardContents);
                Usable = true;
            }
            catch (System.Exception ex)
            {
                Log.Warn(
                    $"OcclusionMaskRT: allocation failed ({ex.GetType().Name}: {ex.Message}). " +
                    "Wall bleed will be unrestricted.");
                Usable = false;
            }
        }

        _allocatedWidth = width;
        _allocatedHeight = height;
    }

    public void Dispose()
    {
        _target?.Dispose();
        _target = null;
        _allocatedWidth = 0;
        _allocatedHeight = 0;
        Usable = false;
    }
}
