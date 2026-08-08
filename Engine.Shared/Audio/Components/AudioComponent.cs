using System.Collections.Generic;
using Engine.Shared.Assets;
using Engine.Shared.GameObjects;

namespace Engine.Shared.Audio;

/// <summary>
/// Attaches a sound to an entity. Managed by <seealso cref="SharedAudioSystem"/>.
/// </summary>
[RegisterComponent("Audio")]
public sealed class AudioComponent : Component
{
    /// <summary>
    /// Relative path (without extension) under the "Audio" resource root, e.g. "SFX/campfire_loop".
    /// </summary>
    [AudioKey]
    public string Key { get; set; } = "";

    public float Volume { get; set; } = 1f;

    /// <summary>
    /// Pitch range: -1 (one octave down) to 1 (one octave up).
    /// </summary>
    public float Pitch { get; set; } = 0f;

    public bool Loop { get; set; } = false;

    /// <summary>
    /// Start playing the moment this component is added, instead of waiting for AudioSystem.Play().
    /// </summary>
    public bool AutoPlay { get; set; } = true;

    /// <summary>
    /// When true, AudioSystem attenuates volume and pans based on distance from the listener.
    /// </summary>
    public bool Spatial { get; set; } = false;

    /// <summary>
    /// World-units distance at which a spatial sound has faded out completely.
    /// </summary>
    public float MaxDistance { get; set; } = 1000f;

    /// <summary>
    /// Any number of tags (see AudioTagPrototype). Each tag has its own runtime-adjustable
    /// volume multiplier
    /// </summary>
    public HashSet<ProtoId<AudioTagPrototype>> Tags { get; set; } = new();

    /// <summary>
    /// Do not set or get this manually.
    /// </summary>
    public float? Elapsed { get; internal set; }
}
