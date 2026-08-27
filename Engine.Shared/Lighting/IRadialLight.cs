using Microsoft.Xna.Framework;

namespace Engine.Shared.Lighting;

/// <summary>
/// Shared falloff/shadow properties of any light that radiates from a point
/// (<see cref="PointLightComponent"/>, <see cref="SpotLightComponent"/>).
/// </summary>
public interface IRadialLight
{
    Color Color { get; }
    float Radius { get; }
    float Intensity { get; }
    bool CastShadows { get; }
    float Softness { get; }
    FalloffMode Falloff { get; }
}
