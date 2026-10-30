#include "Sprite.fxh"

// What a sprite with no shader of its own is drawn with: the sprite as it is, lit.

float4 MainPS(SpritePixel input) : COLOR
{
    return SampleSprite(input);
}

SPRITE_TECHNIQUE(MainPS)
