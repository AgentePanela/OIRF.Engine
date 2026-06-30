using Engine.Shared.GameObjects;
using Microsoft.Xna.Framework;

namespace Engine.Shared.GameObjects.Components.Lighting;

/// <summary>
/// Represents an ambient light source. Acts as the base color/intensity of
/// the lighting buffer where no other light reaches. When multiple are
/// present, the one with the highest <see cref="Priority"/> wins.
/// </summary>
[RegisterComponent("AmbientLight")]
public sealed class AmbientLightComponent : Component
{
    public Color Color { get; set; } = new Color(40, 40, 50);
    public float Intensity { get; set; } = 0.2f;

    /// <summary>
    /// Tiebreaker when several ambient lights exist in the same scene.
    /// </summary>
    public int Priority { get; set; } = 0;
}
