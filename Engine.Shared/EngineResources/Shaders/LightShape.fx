#include "Sprite.fxh"

// One light, drawn by the renderer into the light map (additive). The quad is the light, 2 radius wide, turned with
// it, so LocalUV runs over the whole light and +x is where its cone points.
//
// Color: the light color. SpriteTexture: the mask, only its alpha is used (a white texel for no mask).
// Custom0: x brightness (already divided by the overbright scale), y half the cone angle in radians (pi or more for
// no cone), z cone softness 0..1.
// Custom1: x falloff, y curve factor (see LightingSystem).

float4 LightPS(SpritePixel input) : COLOR0
{
    float2 p = input.LocalUV * 2.0 - 1.0;

    // attenuation curve from https://lisyarus.github.io/blog/posts/point-light-attenuation.html
    float s = saturate(length(p));
    float s2 = s * s;
    float curve = lerp(s, s2, saturate(input.Custom1.y));
    float attenuation = ((1.0 - s2) * (1.0 - s2)) / (1.0 + input.Custom1.x * curve);

    float halfAngle = input.Custom0.y;
    float edge = max(halfAngle * input.Custom0.z, 0.0001);
    float cone = 1.0 - smoothstep(halfAngle - edge, halfAngle, abs(atan2(p.y, p.x)));
    cone = halfAngle >= 3.14159 ? 1.0 : cone;

    float mask = tex2D(SpriteTextureSampler, input.UV).a;

    float value = attenuation * cone * mask * input.Custom0.x;
    return float4(input.Color.rgb * value, 1.0);
}

SPRITE_TECHNIQUE_UNLIT(LightPS)
