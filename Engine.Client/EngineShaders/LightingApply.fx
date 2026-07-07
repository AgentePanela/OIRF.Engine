#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

// Final composite: multiplies the rendered scene by the lightmap. Ambient
// is already baked into the lightmap clear, so there's nothing to add here.

// SceneTexture: the frame as drawn by SpriteBatch (set by RenderManager).
Texture2D SceneTexture;

// LightMap: the lighting buffer — lit pixels are bright, unlit are dark.
Texture2D LightMap;

sampler2D SceneSampler = sampler_state
{
    Texture = <SceneTexture>;
};

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

// Size of one lightmap texel in UV space (1/lightmapW, 1/lightmapH).
// Used by PixelatedLight to snap UVs to texel centres.
float2 LightmapTexelSize = float2(1.0, 1.0);

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float4 MainPS(VertexShaderOutput input) : COLOR
{
    float4 scene = tex2D(SceneSampler, input.TextureCoordinates);
    float3 light = tex2D(LightSampler, input.TextureCoordinates).rgb;
    return float4(scene.rgb * light, scene.a);
}

float4 MainPS_Pixel(VertexShaderOutput input) : COLOR
{
    float4 scene = tex2D(SceneSampler, input.TextureCoordinates);
    // snap to the center of the lightmap texel that owns this screen pixel,
    // so each pixel reads exactly one texel (no mixels)
    float2 uv = floor(input.TextureCoordinates / LightmapTexelSize) * LightmapTexelSize + LightmapTexelSize * 0.5;
    float3 light = tex2D(LightSamplerPoint, uv).rgb;
    return float4(scene.rgb * light, scene.a);
}

technique SpriteDrawing
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
};

technique PixelatedLight
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL MainPS_Pixel();
    }
};
