using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Shadow map render target. One row per shadow-casting light, each row is
/// a 360° unwrap of occluder distances around that light. The depth buffer
/// is what keeps the closest occluder per angle.
///
/// Prefers Single (32-bit float) so the stored distance doesn't quantize to
/// 256 steps like an 8-bit channel would; falls back to Color when the
/// driver refuses it. Allocation failures surface via <see cref="Usable"/>.
/// </summary>
internal sealed class ShadowMapRT
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

        // Depth16 is enough, the stored distance is normalized to [0..1]
        try
        {
            _target = new RenderTarget2D(
                device, width, height, false,
                SurfaceFormat.Single, DepthFormat.Depth16,
                0, RenderTargetUsage.DiscardContents);
            Usable = true;
        }
        catch (System.Exception)
        {
            try
            {
                _target = new RenderTarget2D(
                    device, width, height, false,
                    SurfaceFormat.Color, DepthFormat.Depth16,
                    0, RenderTargetUsage.DiscardContents);
                Usable = true;
                Log.Debug("ShadowMapRT: Single format not supported, using Color (8-bit shadow depth).");
            }
            catch (System.Exception ex)
            {
                Log.Warn(
                    $"ShadowMapRT: allocation failed ({ex.GetType().Name}: {ex.Message}). " +
                    "Shadow casting disabled for this session.");
                Dispose();
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
