#if OPENGL
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

// Point/spot light shader. Draws one light disk into the lightmap with
// shadows sampled from the 1D shadow map. Based on Robust Toolbox's
// light-soft.swsl. Fed via DrawUserPrimitives with world-space quads.

float4x4 viewProj;

// per-light uniforms
float2 lightCenter;
float4 lightColor;
float  lightRange;
float  lightPower;
float  lightSoftness;
float  lightFalloff;
float  lightCurveFactor;
float  lightIndex;      // normalized V coord (0..1) into the shadow map, -1 = no shadow
float2 shadowMapTexel;  // 1 / shadow map size
float  shadowContactBias;

// spotlight only
float  lightDirection;     // cone center angle, radians, 0 = +X
float  lightConeAngle;     // cone half-angle, radians
float  lightConeSoftness;  // edge softness, radians

Texture2D ShadowMap;

sampler2D ShadowSampler = sampler_state
{
    Texture = <ShadowMap>;
    MinFilter = Point;
    MagFilter = Point;
    AddressU = Wrap;
    AddressV = Clamp;
};

struct VSIn
{
    float2 WorldPos    : POSITION0;
};

struct VSOut
{
    float4 Position : SV_POSITION;
    float2 WorldPos : TEXCOORD0;
};

VSOut MainVS(VSIn input)
{
    VSOut o;
    o.Position = mul(float4(input.WorldPos.xy, 0.0, 1.0), viewProj);
    o.WorldPos = input.WorldPos;
    return o;
}

float ShadowDepthAt(float2 rel, float texelOffset)
{
    float deflect = atan2(rel.y, -rel.x) / 3.14159265; // -1..+1
    float u = deflect * 0.5 + 0.5 + texelOffset * shadowMapTexel.x;
    return tex2D(ShadowSampler, float2(u, lightIndex)).r;
}

float ShadowVisibility(float2 rel, float texelOffset)
{
    float blocker = ShadowDepthAt(rel, texelOffset);
    if (blocker >= 0.999)
        return 1.0;

    float receiver = length(rel) / max(lightRange, 0.0001);
    return receiver <= blocker - shadowContactBias ? 1.0 : 0.0;
}

// 7-tap PCF, kernel widens with distance past the blocker for a fake penumbra
float CreateOcclusion(float2 diff)
{
    if (lightIndex < 0.0)
        return 1.0;

    float center = ShadowDepthAt(diff, 0.0);
    if (center >= 0.999)
        return 1.0;

    float receiver = length(diff) / max(lightRange, 0.0001);
    float penumbra = saturate((receiver - center) * 18.0);
    float radius = max(0.25, lightSoftness) * lerp(0.75, 3.0, penumbra);

    float occ = ShadowVisibility(diff, 0.0) * 0.28;
    occ += ShadowVisibility(diff, -radius) * 0.20;
    occ += ShadowVisibility(diff,  radius) * 0.20;
    occ += ShadowVisibility(diff, -radius * 2.0) * 0.12;
    occ += ShadowVisibility(diff,  radius * 2.0) * 0.12;
    occ += ShadowVisibility(diff, -radius * 3.0) * 0.04;
    occ += ShadowVisibility(diff,  radius * 3.0) * 0.04;

    return saturate(occ);
}

// cone attenuation, 1 inside, 0 outside, smooth edge. The pixel at the
// light center is always lit since direction is undefined there.
float ConeFactor(float2 diff)
{
    float len = length(diff);
    if (len < 0.0001)
        return 1.0;

    float2 dirVec = float2(cos(lightDirection), sin(lightDirection));
    float2 nrm = diff / len;
    float angleToPixel = acos(clamp(dot(dirVec, nrm), -1.0, 1.0));

    if (angleToPixel > lightConeAngle)
        return 0.0;

    return 1.0 - smoothstep(lightConeAngle - lightConeSoftness, lightConeAngle, angleToPixel);
}

float4 MainPS(VSOut input) : COLOR0
{
    float2 diff = input.WorldPos - lightCenter;
    float ourDist = length(diff);

    if (ourDist > lightRange)
        discard;

    float occlusion = CreateOcclusion(diff);
    if (occlusion <= 0.001)
        discard;

    // attenuation curve from
    // https://lisyarus.github.io/blog/posts/point-light-attenuation.html
    float s = saturate(ourDist / lightRange);
    float s2 = s * s;
    float curve = lerp(s, s2, saturate(lightCurveFactor));
    float atten = ((1.0 - s2) * (1.0 - s2)) / (1.0 + lightFalloff * curve);

    float val = atten * lightPower * occlusion;

    // rgb = premultiplied contribution, a = strength; blended One,One additive
    return float4(lightColor.rgb * val, val);
}

technique LightSoft
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL MainPS();
    }
};

// single shadow sample, no PCF. cheaper, hard edges
float4 MainPS_Hard(VSOut input) : COLOR0
{
    float2 diff = input.WorldPos - lightCenter;
    float ourDist = length(diff);

    if (ourDist > lightRange)
        discard;

    float occlusion;
    if (lightIndex < 0.0)
        occlusion = 1.0;
    else
        occlusion = ShadowVisibility(diff, 0.0);

    if (occlusion <= 0.001)
        discard;

    float s = saturate(ourDist / lightRange);
    float s2 = s * s;
    float curve = lerp(s, s2, saturate(lightCurveFactor));
    float atten = ((1.0 - s2) * (1.0 - s2)) / (1.0 + lightFalloff * curve);

    float val = atten * lightPower * occlusion;
    return float4(lightColor.rgb * val, val);
}

technique LightHard
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL MainPS_Hard();
    }
};

// spotlight variants, same math gated by ConeFactor
float4 MainPS_Spot(VSOut input) : COLOR0
{
    float2 diff = input.WorldPos - lightCenter;
    float ourDist = length(diff);

    if (ourDist > lightRange)
        discard;

    float cone = ConeFactor(diff);
    if (cone <= 0.001)
        discard;

    float occlusion = CreateOcclusion(diff);
    if (occlusion <= 0.001)
        discard;

    float s = saturate(ourDist / lightRange);
    float s2 = s * s;
    float curve = lerp(s, s2, saturate(lightCurveFactor));
    float atten = ((1.0 - s2) * (1.0 - s2)) / (1.0 + lightFalloff * curve);

    float val = atten * lightPower * occlusion * cone;
    return float4(lightColor.rgb * val, val);
}

technique SpotLightSoft
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL MainPS_Spot();
    }
};

float4 MainPS_SpotHard(VSOut input) : COLOR0
{
    float2 diff = input.WorldPos - lightCenter;
    float ourDist = length(diff);

    if (ourDist > lightRange)
        discard;

    float cone = ConeFactor(diff);
    if (cone <= 0.001)
        discard;

    float occlusion;
    if (lightIndex < 0.0)
        occlusion = 1.0;
    else
        occlusion = ShadowVisibility(diff, 0.0);

    if (occlusion <= 0.001)
        discard;

    float s = saturate(ourDist / lightRange);
    float s2 = s * s;
    float curve = lerp(s, s2, saturate(lightCurveFactor));
    float atten = ((1.0 - s2) * (1.0 - s2)) / (1.0 + lightFalloff * curve);

    float val = atten * lightPower * occlusion * cone;
    return float4(lightColor.rgb * val, val);
}

technique SpotLightHard
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL MainPS_SpotHard();
    }
};
