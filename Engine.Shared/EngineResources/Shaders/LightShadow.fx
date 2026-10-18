// The shadows of the lights of an atlas page (see ShadowBatch). Every quad is one edge of an occluder stretched away
// from its light; this works out, per pixel, how much of the light source the edge hides, and writes it to the alpha
// of the page. Not a sprite shader.

#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

// The page being drawn into, in pixels. Set by the renderer.
float2 PageSize = float2(2048.0, 2048.0);

struct ShadowVertex
{
    float4 Position : POSITION0; // world
    float4 Segment  : TEXCOORD0; // the edge: a.xy, b.xy
    float4 Light    : TEXCOORD1; // center.xy, source size
    float4 Slot     : TEXCOORD2; // the light's center in the page .xy, page pixels per world unit, half its square
};

struct ShadowPixel
{
    float4 Position : SV_POSITION;
    float2 World    : TEXCOORD0;
    float4 Segment  : TEXCOORD1;
    float4 Light    : TEXCOORD2;
    float4 Slot     : TEXCOORD3;
    float2 Page     : TEXCOORD4;
};

ShadowPixel ShadowVS(ShadowVertex input)
{
    ShadowPixel output;

    float2 page = input.Slot.xy + (input.Position.xy - input.Light.xy) * input.Slot.z;
    output.Position = float4(page.x / PageSize.x * 2.0 - 1.0, 1.0 - page.y / PageSize.y * 2.0, 0.0, 1.0);
    output.World = input.Position.xy;
    output.Segment = input.Segment;
    output.Light = input.Light;
    output.Slot = input.Slot;
    output.Page = page;
    return output;
}

// The angle of v around p, from the direction to the light d.
float AngleFrom(float2 d, float2 v)
{
    return atan2(d.x * v.y - d.y * v.x, dot(d, v));
}

float4 ShadowPS(ShadowPixel input) : COLOR0
{
    // only inside the square of its own light
    clip(input.Page - (input.Slot.xy - input.Slot.w));
    clip((input.Slot.xy + input.Slot.w) - input.Page);

    float2 p = input.World;
    float2 a = input.Segment.xy;
    float2 b = input.Segment.zw;
    float2 light = input.Light.xy;

    // only behind the edge: the light and the pixel on opposite sides of its line
    float2 edge = b - a;
    float lightSide = edge.x * (light.y - a.y) - edge.y * (light.x - a.x);
    float pixelSide = edge.x * (p.y - a.y) - edge.y * (p.x - a.x);
    clip(-lightSide * pixelSide);

    // seen from the pixel, the source is a range of angles around the direction to its center, and the edge another
    float2 toLight = light - p;
    float distance = max(length(toLight), 0.0001);
    float2 d = toLight / distance;

    float angleA = AngleFrom(d, a - p);
    float angleB = AngleFrom(d, b - p);
    float low = min(angleA, angleB);
    float high = max(angleA, angleB);

    // the part of the source the edge covers: all of it is the full shadow, some of it the soft edge
    float halfSource = atan(input.Light.z / distance);
    float soft = saturate((min(high, halfSource) - max(low, -halfSource)) / max(halfSource * 2.0, 0.0001));
    float hard = low <= 0.0 && high >= 0.0 ? 1.0 : 0.0;

    return float4(0.0, 0.0, 0.0, halfSource > 0.0005 ? soft : hard);
}

technique Shadow
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL ShadowVS();
        PixelShader = compile PS_SHADERMODEL ShadowPS();
    }
}
