using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Single-channel alpha render target used as an occlusion mask. Built
/// once per frame by summing per-light "is this pixel lit?" contributions
/// from <see cref="OcclusionMask.fx"/>. The wall-bleed pass samples it to
/// restrict the blur-add to pixels actually reached by a shadow-casting
/// light, instead of bleeding light everywhere like the original
/// fullscreen-add approach.
///
/// We use <see cref="SurfaceFormat.Alpha8"/> because the mask only needs
/// a single channel — additive blending of 0..1 contributions is enough.
/// On Reach profiles where Alpha8 isn't supported we fall back to Color.
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
    /// Resize the target to match the lightmap. Cheap to call every frame —
    /// only allocates on size change.
    /// </summary>
    public void EnsureSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        if (_target is not null && width == _allocatedWidth && height == _allocatedHeight)
            return;

        Dispose();

        var device = GameClient.GraphicsDevice;

        // Prefer Alpha8 (1 byte/pixel, ~4× cheaper than Color). Fall back
        // to Color if the device rejects it.
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
