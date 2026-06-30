using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics.Lighting;

/// <summary>
/// Owns the shadow map render target. The shadow map is a 1D-unwrapped
/// cylindrical depth texture where each row corresponds to one
/// shadow-casting light and each column is an angle (-π to +π) around it.
///
/// Format: <see cref="SurfaceFormat.Color"/> for the color attachment,
/// with a 16-bit depth attachment used purely as a "minimum distance
/// per pixel" sort. The fragment shader writes (dist/r, dist²/r²) into
/// RG; depth is what enforces the "min" across overlapping occluder
/// slices.
///
/// MonoGame's <see cref="RenderTarget2D"/> does not expose the depth-stencil
/// attachment publicly, so we can't reliably introspect whether the driver
/// actually gave us one. Instead, this class catches allocation exceptions
/// and exposes a <see cref="Usable"/> flag the lighting system can check
/// before drawing.
/// </summary>
internal sealed class ShadowMapRT
{
    private RenderTarget2D? _target;
    private int _allocatedWidth;
    private int _allocatedHeight;

    public RenderTarget2D? Target => _target;

    public int Width => _allocatedWidth;
    public int Height => _allocatedHeight;

    /// <summary>
    /// True when the backing texture was allocated successfully. The lighting
    /// system should skip the shadow pass entirely if this is false (e.g.
    /// driver refused Depth16 on this surface format).
    /// </summary>
    public bool Usable { get; private set; }

    /// <summary>
    /// Make sure the backing textures match the requested dimensions.
    /// Cheap to call every frame — only allocates when size changes.
    /// </summary>
    public void EnsureSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        if (_target is not null && width == _allocatedWidth && height == _allocatedHeight)
            return;

        Dispose();

        var device = GameClient.GraphicsDevice;

        // MonoGame creates the depth-stencil buffer internally when the
        // DepthFormat is set on the RenderTarget2D ctor. Depth16 is
        // plenty for our normalized [0..1] distance. Some drivers refuse
        // certain surface-format + depth-format combos — we surface that
        // as Usable=false so the lighting system can skip drawing into it.
        try
        {
            _target = new RenderTarget2D(
                device,
                width,
                height,
                false,
                SurfaceFormat.Color,
                DepthFormat.Depth16,
                0,
                RenderTargetUsage.PreserveContents);
            Usable = true;
        }
        catch (System.Exception ex)
        {
            Log.Warn(
                $"ShadowMapRT: allocation failed ({ex.GetType().Name}: {ex.Message}). " +
                "Shadow casting disabled for this session.");
            Dispose();
            Usable = false;
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

