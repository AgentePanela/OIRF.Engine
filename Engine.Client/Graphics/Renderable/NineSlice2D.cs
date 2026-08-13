using Engine.Client.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Client.Graphics;

/// <summary>
/// A stretchy, corner-preserving texture renderable (9-slice) - corners stay native size,
/// edges stretch along one axis, center stretches on both. See
/// <see cref="NineSlicePatch.Compute"/> for the actual slicing math this draws with.
/// </summary>
public struct NineSlice2D : IRenderable
{
    public int Layer { get; set; }
    public SamplerState? SamplerState { get; set; }

    /// <summary>
    /// Atlas sprite to slice - resolved the same way any other Sprite2D is (cached texture/region
    /// if SpriteSystem already set them, otherwise a lookup by Key).
    /// </summary>
    public Sprite2D Sprite;

    /// <summary>Cut margins, in source pixels, defining the 3x3 grid.</summary>
    public Thickness Margin;

    /// <summary>Final drawn size. Draw's position is the top-left corner.</summary>
    public Vector2 Size;

    public Color Tint = Color.White;
    public float Depth { get; set; }
    public bool Visible = true;

    public NineSlice2D(Sprite2D sprite, Thickness margin, Vector2 size)
    {
        Sprite = sprite;
        Margin = margin;
        Size = size;
    }

    public void Draw(RenderManager renderer, Vector2 pos) => renderer.DrawNineSlice(this, pos);
}
