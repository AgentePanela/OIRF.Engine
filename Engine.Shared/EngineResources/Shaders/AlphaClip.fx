#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

// Substituted in place of a null shader for default (no custom shader) sprites
// while RenderManager is writing the lighting stencil buffer. Plain textured
// draw, identical to SpriteBatch's own default effect, plus a clip() so fully
// transparent atlas padding never reaches the stencil write stage - see
// RenderManager.GetAlphaClipEffect and DrawRenderQueue.

Texture2D SpriteTexture;

sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float4 MainPS(VertexShaderOutput input) : COLOR
{
    float4 color = tex2D(SpriteTextureSampler, input.TextureCoordinates) * input.Color;
    clip(color.a - 0.001);
    return color;
}

technique AlphaClip
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
};
