using Engine.Shared.Graphics;
using Microsoft.Xna.Framework;

namespace Engine.Client.Graphics;

// only exist because sprite comp is shared :p

public struct TextureLighting
{
    public LightBaseMode Mode;

    /// <summary>
    /// World position of the base, for <see cref="LightBaseMode.Line"/> and <see cref="LightBaseMode.Point"/>.
    /// </summary>
    public Vector2 Base;

    /// <summary>
    /// 0 is lit, 1 unlit
    /// </summary>
    public float Emission;

    public TextureLighting(LightBaseMode mode, Vector2 @base, float emission)
    {
        Mode = mode;
        Base = @base;
        Emission = emission;
    }

    /// <summary>
    /// Drawn at full brightness whatever the light.
    /// </summary>
    public static TextureLighting Unshaded => new(LightBaseMode.Pixel, Vector2.Zero, 1f);
}
