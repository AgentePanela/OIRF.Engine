using Engine.Shared.GameObjects;

namespace Engine.Client.Graphics;

[RegisterComponent("Animation")]
public class AnimationComponent : Component
{
    /// <summary>
    /// Key of the animation to play, e.g. "Player/walk-anim". Must match an id defined
    /// in that folder's info.yml. Independent from SpriteComponent.Key, which gets
    /// overwritten with the current frame's key while this animation is playing.
    /// </summary>
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
}
