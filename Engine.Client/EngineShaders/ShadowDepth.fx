#if OPENGL
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

// Shadow depth shader, ported from Clyde. Occluders come in as edges
// (4 verts each); the VS projects the edge into angular space and stretches
// it across the shadow map row, the PS ray-casts against the edge to get
// the exact distance per angular texel. The LESS depth test keeps the
// closest occluder per angle.

float2 lightPos;
float  lightRadius;
float  shadowWrapPass;

struct VSIn
{
    float4 aPos      : POSITION0;   // .xy = endpoint A, .zw = endpoint B (world space)
    float2 subVertex : TEXCOORD0;   // .x = 0/1 (A/B), .y = 0/1 (top/bottom)
};

struct VSOut
{
    float4 Position : SV_POSITION;
    float  Angle    : TEXCOORD0;   // normalized angle, -1..+1
    float4 Edge     : TEXCOORD1;   // endpoints, world space
};

VSOut MainVS(VSIn input)
{
    float2 relA = input.aPos.xy - lightPos;
    float2 relB = input.aPos.zw - lightPos;

    float angleA = atan2(relA.y, -relA.x) / 3.14159265;
    float angleB = atan2(relB.y, -relB.x) / 3.14159265;
    float span = angleB - angleA;

    // always take the shorter angular interval. Edges that cross the
    // -pi/+pi seam get drawn twice, the wrap pass covers the tail
    float wrapOffset = 0.0;
    if (span > 1.0)
    {
        angleB -= 2.0;
        wrapOffset = 2.0;
    }
    else if (span < -1.0)
    {
        angleB += 2.0;
        wrapOffset = -2.0;
    }

    float angle = lerp(angleA, angleB, input.subVertex.x);
    if (shadowWrapPass > 0.5)
        angle += wrapOffset != 0.0 ? wrapOffset : 4.0; // +4 pushes non-wrapping edges off screen

    float2 endpoint = input.subVertex.x < 0.5 ? relA : relB;
    float ndcZ = saturate(length(endpoint) / max(lightRadius, 0.0001));
    float yClip = input.subVertex.y < 0.5 ? -1.0 : 1.0;

    VSOut output;
    output.Position = float4(angle, yClip, ndcZ, 1.0);
    output.Angle    = angle;
    output.Edge     = input.aPos;
    return output;
}

float4 MainPS(VSOut input) : COLOR0
{
    float angle = input.Angle * 3.14159265;
    float2 rayDir = float2(-cos(angle), sin(angle));

    float2 a = input.Edge.xy - lightPos;
    float2 b = input.Edge.zw - lightPos;
    float2 edge = b - a;

    // ray vs segment intersection
    float denom = rayDir.x * edge.y - rayDir.y * edge.x;
    if (abs(denom) < 0.00001)
        discard;

    float crossAEdge = a.x * edge.y - a.y * edge.x;
    float crossARay = a.x * rayDir.y - a.y * rayDir.x;
    float rayDistance = crossAEdge / denom;
    float edgeFraction = crossARay / denom;

    if (rayDistance <= 0.0 || edgeFraction < -0.001 || edgeFraction > 1.001)
        discard;

    // R = occluder distance, normalized to the light radius
    float d = saturate(rayDistance / max(lightRadius, 0.0001));
    return float4(d, 0.0, 0.0, 1.0);
}

technique ShadowDepth
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader  = compile PS_SHADERMODEL MainPS();
    }
};
