using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;

namespace Engine.Shared.Lighting;

/// <summary>
/// Represents an ambient light source. Acts as the base color/intensity of
/// the lighting buffer where no other light reaches.
/// </summary>
[RegisterComponent("AmbientLight"), NetworkedComponent]
public sealed partial class AmbientLightComponent : Component
{
    [NetField] public Color Color { get; set; } = new Color(40, 40, 50);
    [NetField] public float Intensity { get; set; } = 0.2f;

    /// <summary>
    /// Tiebreaker when several ambient lights exist in the same scene.
    /// </summary>
    [NetField] public int Priority { get; set; } = 0;
}
