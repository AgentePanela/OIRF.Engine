#include "Sprite.fxh"

// Custom0.x: the brightness the bloom starts at, 0 to only shrink the image.

float4 ExtractPS(SpritePixel input) : COLOR0
{
    // drawn at half size: 4 linear samples average 4x4 pixels
    float2 t = TexelSize;
    float3 color = (tex2D(SpriteTextureSampler, input.UV + float2(-t.x, -t.y)).rgb
                  + tex2D(SpriteTextureSampler, input.UV + float2(t.x, -t.y)).rgb
                  + tex2D(SpriteTextureSampler, input.UV + float2(-t.x, t.y)).rgb
                  + tex2D(SpriteTextureSampler, input.UV + float2(t.x, t.y)).rgb) * 0.25;

    float bright = max(color.r, max(color.g, color.b));
    float keep = max(bright - input.Custom0.x, 0.0) / max(bright, 0.0001);
    return float4(color * keep, 1.0);
}

SPRITE_TECHNIQUE_UNLIT(ExtractPS)
