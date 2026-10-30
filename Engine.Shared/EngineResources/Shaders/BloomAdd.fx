#include "Sprite.fxh"

// Custom0.x: how strong the glow is added.

float4 AddPS(SpritePixel input) : COLOR0
{
    return float4(tex2D(SpriteTextureSampler, input.UV).rgb * input.Custom0.x, 1.0);
}

SPRITE_TECHNIQUE_UNLIT(AddPS)
