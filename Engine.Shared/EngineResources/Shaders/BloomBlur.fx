#include "Sprite.fxh"

// Custom0.xy: the direction of the blur, in texels.

float4 BlurPS(SpritePixel input) : COLOR0
{
    // 9 taps of a gaussian in 5 linear samples
    float2 step = input.Custom0.xy * TexelSize;
    float3 color = tex2D(SpriteTextureSampler, input.UV).rgb * 0.2270270270;
    color += (tex2D(SpriteTextureSampler, input.UV + step * 1.3846153846).rgb
            + tex2D(SpriteTextureSampler, input.UV - step * 1.3846153846).rgb) * 0.3162162162;
    color += (tex2D(SpriteTextureSampler, input.UV + step * 3.2307692308).rgb
            + tex2D(SpriteTextureSampler, input.UV - step * 3.2307692308).rgb) * 0.0702702703;
    return float4(color, 1.0);
}

SPRITE_TECHNIQUE_UNLIT(BlurPS)
