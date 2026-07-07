#if OPENGL
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

// Wall bleed merge. Draws the occluder quads over the lightmap, replacing
// each wall pixel with the blurred lightmap value at that spot, so walls
// show the averaged glow of nearby lights instead of staying black (same
// as Robust Toolbox's MergeWallLayer, which draws wall geometry with
// One/Zero blending).

Texture2D BlurredLightMap;
float4x4  viewProj;

sampler2D BlurredSampler = sampler_state
{
    Texture = <BlurredLightMap>;
    MinFilter = Linear;
    MagFilter = Linear;
    AddressU  = Clamp;
    AddressV  = Clamp;
};

struct VSIn
{
    float2 WorldPos : POSITION0;
};

struct VSOut
{
    float4 Position  : SV_POSITION;
    float4 ScreenPos : TEXCOORD0;
};

VSOut MainVS(VSIn input)
{
    VSOut o;
    o.Position  = mul(float4(input.WorldPos.xy, 0.0, 1.0), viewProj);
    o.ScreenPos = o.Position;
    return o;
}

float4 MainPS(VSOut input) : COLOR0
{
    // clip space -> lightmap uv (ndc y +1 is the top of the target)
    float2 ndc = input.ScreenPos.xy / input.ScreenPos.w;
    float2 uv = float2(ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5);
    return float4(tex2D(BlurredSampler, uv).rgb, 1.0);
}

technique WallMerge
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL MainPS();
    }
};
