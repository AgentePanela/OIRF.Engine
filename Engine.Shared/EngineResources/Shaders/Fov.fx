#include "Sprite.fxh"

// What covers what the eye of a viewport does not see. Drawn by the renderer as one quad over the world, in world
// space: each pixel reads the field of view where it is (Light.xy, a Pixel sprite).

// What the eye did not see, in the alpha (1 is hidden). Set by the renderer.
Texture2D FovMask;

sampler2D FovMaskSampler : register(s3) = sampler_state
{
    Texture = <FovMask>;
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};

// Takes a world position to its uv on FovMask: uv = position * xy + zw.
float4 FovMaskTransform = float4(0.0, 0.0, 0.0, 0.0);

// What is drawn where the eye cannot see, premultiplied.
float4 FovColor = float4(0.0, 0.0, 0.0, 1.0);

// xy: the eye. z: how far it sees, 0 for no limit. w: how wide the edge of that limit fades.
float4 FovEye = float4(0.0, 0.0, 0.0, 1.0);

float4 FovPS(SpritePixel input) : COLOR0
{
    float2 at = input.Light.xy;
    float hidden = tex2D(FovMaskSampler, at * FovMaskTransform.xy + FovMaskTransform.zw).a;

    float distance = length(at - FovEye.xy);
    float far = smoothstep(FovEye.z - max(FovEye.w, 0.0001), FovEye.z, distance);
    hidden = max(hidden, FovEye.z > 0.0 ? far : 0.0);

    return FovColor * hidden;
}

SPRITE_TECHNIQUE_UNLIT(FovPS)
