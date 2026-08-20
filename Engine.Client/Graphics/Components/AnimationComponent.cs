using System.Collections.Generic;
using Engine.Shared.Assets;
using Engine.Shared.GameObjects;

namespace Engine.Client.Graphics;

[RegisterComponent("Animation")]
public class AnimationComponent : Component
{
    /// <summary>
    /// Key of the animation to play, e.g. "Player/walk-anim". Must match an id defined
    /// in that folder's info.yml. Independent from SpriteComponent.Key, which gets
    /// overwritten with the current frame's key while this animation is playing.
    /// Leave empty if this entity only animates via <see cref="Layers"/>.
    /// </summary>
    [AnimationKey]
    public string Key { get; set; } = string.Empty;

    public bool Playing { get; set; } = true;

    /// <summary>
    /// Per-entity speed override, in frames per second. Null means "use the speed from info.yml".
    /// Use AnimationSystem.SetSpeed to change it.
    /// </summary>
    public float? SpeedOverride { get; set; } = null;

    /// <summary>
    /// Per-entity loop override. Null means "use the value from info.yml".
    /// Use AnimationSystem.SetLoop to change it.
    /// </summary>
    public bool? LoopOverride { get; set; } = null;

    /// <summary>
    /// Do not set manually. Use AnimationSystem.
    /// </summary>
    public int CurrentFrame { get; set; } = 0;

    /// <summary>
    /// Do not set manually. Use AnimationSystem.
    /// </summary>
    public float Elapsed { get; set; } = 0f;

    /// <summary>
    /// Per-layer animations, driving individual entries of the sibling SpriteComponent.Layers
    /// instead of the base sprite. Matched by <see cref="LayerAnimation.LayerId"/> against
    /// SpriteLayer.Id; entries whose id doesn't match any layer are skipped.
    /// Use AnimationSystem.SetLayerAnimation to add/change one.
    /// </summary>
    public List<LayerAnimation> Layers { get; set; } = new();
}

/// <summary>
/// Animation state for a single SpriteComponent layer. See AnimationComponent.Layers.
/// </summary>
public class LayerAnimation
{
    /// <summary>
    /// Id of the SpriteLayer (SpriteComponent.Layers) this animation drives.
    /// </summary>
    public string LayerId { get; set; } = string.Empty;

    /// <inheritdoc cref="AnimationComponent.Key"/>
    [AnimationKey]
    public string Key { get; set; } = string.Empty;

    public bool Playing { get; set; } = true;

    /// <inheritdoc cref="AnimationComponent.SpeedOverride"/>
    public float? SpeedOverride { get; set; } = null;

    /// <inheritdoc cref="AnimationComponent.LoopOverride"/>
    public bool? LoopOverride { get; set; } = null;

    /// <summary>
    /// Do not set manually. Use AnimationSystem.
    /// </summary>
    public int CurrentFrame { get; set; } = 0;

    /// <summary>
    /// Do not set manually. Use AnimationSystem.
    /// </summary>
    public float Elapsed { get; set; } = 0f;
}
