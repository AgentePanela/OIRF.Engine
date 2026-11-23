#include "Sprite.fxh"
#include "FovMask.fxh"

// The default way what the eye of a viewport does not see is drawn (ShadowFov): fov.color over it. Drawn by the
// renderer as one quad over the target, each pixel reading the field of view where it is in the world (Light.xy).

float4 FovPS(SpritePixel input) : COLOR0
{
    return FovColor * FovHidden(input.Light.xy);
}

SPRITE_TECHNIQUE_UNLIT(FovPS)
