// How a sprite takes the light. Included by Sprite.fxh: SPRITE_TECHNIQUE runs ApplyLighting on what a shader returns.

#ifndef ENGINE_TEXTURE_LIGHTING_FXH
#define ENGINE_TEXTURE_LIGHTING_FXH

// The light of the viewport being drawn, world aligned. Set by the renderer.
Texture2D LightMap;

sampler2D LightMapSampler : register(s1) = sampler_state
{
    Texture = <LightMap>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};

// Takes a world position to its uv on the light map: uv = position * xy + zw.
float4 LightMapTransform = float4(0.0, 0.0, 0.0, 0.0);

// x: 1 turns the light off (nothing is darkened). y: size of the blocks the light is snapped to, in world units, 0
// for smooth light. z: what the light map is multiplied by (it holds light / z, so lights can go over 1). w: 1 to
// dither the light, hiding the banding of the 8 bit light map.
float4 LightParams = float4(1.0, 0.0, 1.0, 0.0);

float3 SampleLight(float2 world)
{
    // centered on its block when the light is pixelated
    float snap = LightParams.y;
    float2 snapped = (floor(world / max(snap, 0.0001)) + 0.5) * snap;
    world = snap > 0.0 ? snapped : world;

    float3 light = tex2D(LightMapSampler, world * LightMapTransform.xy + LightMapTransform.zw).rgb * LightParams.z;

    // half a step of the light map either way, a different amount on every world pixel
    float noise = frac(sin(dot(floor(world), float2(12.9898, 78.233))) * 43758.5453) - 0.5;
    return light + noise * LightParams.w * LightParams.z / 255.0;
}

float4 ApplyLighting(float4 color, SpritePixel input)
{
    float emission = saturate(input.Light.z + LightParams.x);

    // w is the highest the light is read at (a PixelCapped sprite), far up for everything else
    float3 light = SampleLight(float2(input.Light.x, max(input.Light.y, input.Light.w)));

    color.rgb *= lerp(light, float3(1.0, 1.0, 1.0), emission);
    return color;
}

#endif
