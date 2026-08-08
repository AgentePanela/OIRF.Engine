using Engine.Shared.GameObjects;

namespace Engine.Shared.Audio;

/// <summary>
/// Marks an entity as the audio listener for spatial audio (AudioComponent.Spatial).
/// </summary>
[RegisterComponent("AudioListener")]
public sealed class AudioListenerComponent : Component
{
    /// <summary>
    /// Only one active listener is used at a time; first one found wins.
    /// </summary>
    public bool Active { get; set; } = true;
}
