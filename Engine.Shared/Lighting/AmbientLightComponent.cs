using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;

namespace Engine.Shared.Lighting;

/// <summary>
/// Represents an ambient light source. Acts as the base color/intensity of
/// the lighting buffer where no other light reaches.
/// </summary>
[RegisterComponent("AmbientLight"), NetworkedComponent, AutoDirty]
public sealed partial class AmbientLightComponent : Component
{
    [NetField] public partial Color Color { get; set; } = new Color(40, 40, 50);
    [NetField] public partial float Intensity { get; set; } = 0.2f;

    /// <summary>
    /// Tiebreaker when several ambient lights exist in the same scene.
    /// </summary>
    [NetField] public partial int Priority { get; set; } = 0;
}
