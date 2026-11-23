// The field of view of the viewport being drawn, for a shader that wants to know what its eye sees. Include it after
// Sprite.fxh; the renderer sets everything here. See Docs/Content/Lighting.md, "Field of view".

#ifndef ENGINE_FOV_MASK_FXH
#define ENGINE_FOV_MASK_FXH

Texture2D FovMask;

sampler2D FovMaskSampler : register(s2) = sampler_state
{
    Texture = <FovMask>;
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};

float4 FovMaskTransform = float4(0.0, 0.0, 0.0, 0.0);

float4 FovColor = float4(0.0, 0.0, 0.0, 1.0);

float4 FovEye = float4(0.0, 0.0, 0.0, 1.0);

float4 FovSample(float2 world)
{
    return tex2D(FovMaskSampler, world * FovMaskTransform.xy + FovMaskTransform.zw);
}

// How hidden a world position is, 0 to 1: the mask and the radius of the eye
float FovHidden(float2 world)
{
    float hidden = FovSample(world).a;
    float far = smoothstep(FovEye.z - max(FovEye.w, 0.0001), FovEye.z, length(world - FovEye.xy));
    return max(hidden, FovEye.z > 0.0 ? far : 0.0);
}

#endif
