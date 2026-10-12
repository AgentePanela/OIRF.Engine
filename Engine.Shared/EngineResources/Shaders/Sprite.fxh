// Everything a sprite shader needs from the engine. Include it, write a pixel shader that takes a SpritePixel and
// close the file with SPRITE_TECHNIQUE(YourPixelShader). See Docs/Content/Shaders.md.

#ifndef ENGINE_SPRITE_FXH
#define ENGINE_SPRITE_FXH

#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

// Set by the renderer, never from a prototype or from code.
float4x4 MatrixTransform;

// The size of one texel of the texture being drawn (the atlas page of a sprite).
float2 TexelSize = float2(0.001, 0.001);

// Seconds since the game started.
float Time = 0.0;

// The size in pixels of the target being drawn into.
float2 ScreenSize = float2(1280.0, 720.0);

Texture2D SpriteTexture;

sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
};

struct SpriteVertex
{
    float4 Position : POSITION0;
    float4 Color    : COLOR0;
    float2 UV       : TEXCOORD0;
    float2 LocalUV  : TEXCOORD1;
    float4 Custom0  : TEXCOORD2;
    float4 Custom1  : TEXCOORD3;
};

struct SpritePixel
{
    float4 Position : SV_POSITION;

    // The tint of the sprite.
    float4 Color    : COLOR0;

    // Where to sample SpriteTexture. Sprites share atlas pages, so this is NOT 0..1 over the sprite.
    float2 UV       : TEXCOORD0;

    // 0..1 over the sprite itself: (0, 0) is its top left, (1, 1) its bottom right.
    float2 LocalUV  : TEXCOORD1;

    // Two free values each sprite carries on its own, see perSprite in the shader prototype.
    float4 Custom0  : TEXCOORD2;
    float4 Custom1  : TEXCOORD3;
};

SpritePixel SpriteVS(SpriteVertex input)
{
    SpritePixel output;
    output.Position = mul(input.Position, MatrixTransform);
    output.Color = input.Color;
    output.UV = input.UV;
    output.LocalUV = input.LocalUV;
    output.Custom0 = input.Custom0;
    output.Custom1 = input.Custom1;
    return output;
}

// The sprite as it would be drawn with no shader at all.
float4 SampleSprite(SpritePixel input)
{
    return tex2D(SpriteTextureSampler, input.UV) * input.Color;
}

#define SPRITE_TECHNIQUE(pixelShader) \
    technique Sprite \
    { \
        pass P0 \
        { \
            VertexShader = compile VS_SHADERMODEL SpriteVS(); \
            PixelShader = compile PS_SHADERMODEL pixelShader(); \
        } \
    }

#endif
