#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

// Substituted in place of a null shader for default (no custom shader)
// sprites while lighting is active, so they sample the lightmap themselves
// instead of relying on a full-screen post-process - see
// RenderManager.GetDefaultLitEffect and DrawRenderQueue. This is the same
// per-sprite sampling that Grayscale.fx/MetallicFloor.fx get injected
// automatically by ShaderLightingInjector; this one is hand-written since
// it has no unique color logic of its own.

Texture2D SpriteTexture;
sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
};

// Set every frame by RenderManager for any shader that declares them.
// LightingEnabled mirrors LightingManager.Enabled - the lightmap sampling
// below must be skipped when it's false, since an unbound LightMap texture
// would otherwise sample as black and multiply every sprite to solid black.
bool LightingEnabled = false;
Texture2D LightMap;
float2 ViewportSize = float2(1280.0, 720.0);
bool PixelatedLighting = false;

sampler2D LightSampler = sampler_state
{
    Texture = <LightMap>;
    MinFilter = Linear;
    MagFilter = Linear;
};

sampler2D LightSamplerPoint = sampler_state
{
    Texture = <LightMap>;
    MinFilter = Point;
    MagFilter = Point;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float4 Lit(VertexShaderOutput input, float2 screenPos)
{
    float4 color = tex2D(SpriteTextureSampler, input.TextureCoordinates) * input.Color;

    // Discard fully-transparent texels (e.g. untrimmed atlas padding) before
    // the stencil write stage - see RenderManager's stencil-based ordering.
    clip(color.a - 0.001);

    // Ternary, not an if() - texture samples inside a real branch are
    // unreliable under ps_3_0/OpenGL. Always sample (cheap, unconditional),
    // then select the result - mirrors the PixelatedLighting ternary below,
    // which already proved this pattern compiles fine on this profile.
    float2 uv = screenPos / ViewportSize;
    float3 sampledLight = PixelatedLighting
        ? tex2D(LightSamplerPoint, uv).rgb
        : tex2D(LightSampler, uv).rgb;
    float3 light = LightingEnabled ? sampledLight : float3(1.0, 1.0, 1.0);

    return float4(color.rgb * light, color.a);
}

// ── OpenGL / ps_3_0 - pixel position via VPOS ───────────────────────────────
#if OPENGL
float4 MainPS(VertexShaderOutput input, float2 vpos : VPOS) : COLOR
{
    return Lit(input, vpos);
}
// ── DirectX / ps_4_0 - pixel position via SV_Position ───────────────────────
#else
float4 MainPS(VertexShaderOutput input) : COLOR
{
    return Lit(input, input.Position.xy);
}
#endif

technique DefaultLit
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
};
