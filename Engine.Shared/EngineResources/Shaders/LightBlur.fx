#if OPENGL
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

// Separable 9-tap Gaussian blur for the lightmap. The engine ping-pongs
// between two render targets, alternating isHorizontal.

Texture2D SourceMap;
float2  SourceTexel;   // 1 / sourceSize
float   isHorizontal;  // 1.0 → blur X, 0.0 → blur Y

sampler2D SourceSampler = sampler_state
{
    Texture = <SourceMap>;
    MinFilter = Linear;
    MagFilter = Linear;
    AddressU  = Clamp;
    AddressV  = Clamp;
};

struct VSIn
{
    float2 Position    : POSITION0;
    float2 TexCoord    : TEXCOORD0;
};

struct VSOut
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

VSOut MainVS(VSIn input)
{
    VSOut o;
    o.Position = float4(input.Position.xy, 0.0, 1.0);
    o.TexCoord = input.TexCoord;
    return o;
}

float4 MainPS(VSOut input) : COLOR0
{
    float2 step = isHorizontal > 0.5
        ? float2(SourceTexel.x, 0.0)
        : float2(0.0, SourceTexel.y);

    float4 c0 = tex2D(SourceSampler, input.TexCoord - step * 4.0);
    float4 c1 = tex2D(SourceSampler, input.TexCoord - step * 3.0);
    float4 c2 = tex2D(SourceSampler, input.TexCoord - step * 2.0);
    float4 c3 = tex2D(SourceSampler, input.TexCoord - step * 1.0);
    float4 c4 = tex2D(SourceSampler, input.TexCoord);
    float4 c5 = tex2D(SourceSampler, input.TexCoord + step * 1.0);
    float4 c6 = tex2D(SourceSampler, input.TexCoord + step * 2.0);
    float4 c7 = tex2D(SourceSampler, input.TexCoord + step * 3.0);
    float4 c8 = tex2D(SourceSampler, input.TexCoord + step * 4.0);

    // binomial weights, sum = 256
    float4 result =
        c0 * 1.0  + c1 * 8.0  + c2 * 28.0 + c3 * 56.0 +
        c4 * 70.0 +
        c5 * 56.0 + c6 * 28.0 + c7 * 8.0  + c8 * 1.0;

    return result / 256.0;
}

technique LightBlur
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL MainPS();
    }
};
