#include "Sprite.fxh"

// What a sprite is drawn with while it writes a mask and has no shader of its own: only the texels that are
// mostly opaque count, so the mask has the shape of the sprite and not of its rectangle.

float4 MainPS(SpritePixel input) : COLOR
{
    float4 color = SampleSprite(input);
    clip(color.a - 0.5);
    return color;
}

SPRITE_TECHNIQUE(MainPS)
