using Microsoft.Xna.Framework;

namespace Engine.Shared.GameObjects.Components.Lighting;

/// <summary>
/// Shared falloff/shadow properties of any light that radiates from a point
/// (<see cref="PointLightComponent"/>, <see cref="SpotLightComponent"/>).
/// Lets <c>LightingSystem</c> collect and shadow-cast both kinds through the
/// same code path.
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
