#include "Sprite.fxh"

// Custom0: the region of the frame on its page, in uv (x, y, width, height).
// Custom1: xy how wide the fading edge is, as a part of the quad; z the brightness (already divided by the overbright).

float4 GlowPS(SpritePixel input) : COLOR0
{
    // 16 samples spread over the frame, the same for every pixel
    float3 sum = float3(0.0, 0.0, 0.0);
    [unroll] for (int y = 0; y < 4; y++)
    {
        [unroll] for (int x = 0; x < 4; x++)
        {
            float2 at = input.Custom0.xy + input.Custom0.zw * ((float2(x, y) + 0.5) / 4.0);
            sum += tex2D(SpriteTextureSampler, at).rgb;
        }
    }

    float2 edge = max(input.Custom1.xy, 0.0001);
    float2 past = max(max(edge - input.LocalUV, input.LocalUV - (1.0 - edge)), 0.0) / edge;
    float fade = saturate(1.0 - length(past));

    float3 light = sum / 16.0 * input.Color.rgb * input.Custom1.z * fade * fade;
    return float4(light, 0.0);
}

SPRITE_TECHNIQUE_UNLIT(GlowPS)
