namespace Engine.Client.Assets.Animation;

/// <summary>
/// Animation metadata loaded from a folder's info.yml. Frame textures themselves
/// live in the atlas under "{AnimKey}-{frame index}" (e.g. "Player/walk-anim-0").
/// </summary>
public sealed class AnimationDef
{
    /// <summary>
    /// Full key of the animation, e.g. "Player/walk-anim". Frame N is stored in the
    /// atlas as "{Key}-{N}".
    /// </summary>
    public required string Key { get; init; }

    public required int FrameCount { get; init; }

    /// <summary>
    /// Default frames per second, used for frames without a FrameSpeeds override.
    /// </summary>
    public float Speed { get; init; } = 10f;

    public bool Loop { get; init; } = true;

    /// <summary>
    /// Optional per-frame duration multiplier (index-aligned with frame number).
    /// A value of 2 makes that frame stay twice as long as the default.
    /// </summary>
    public float[]? FrameSpeeds { get; init; }

    /// <summary>
    /// Seconds the given frame should be displayed for. Pass speedOverride to use a speed
    /// (in frames per second) other than this animation's own, e.g. an AnimationComponent's
    /// per-entity SpeedOverride.
    /// </summary>
    public float GetFrameDuration(int frame, float? speedOverride = null)
    {
        var speed = speedOverride ?? Speed;
        var baseDuration = speed > 0f ? 1f / speed : float.PositiveInfinity;
        if (FrameSpeeds is not null && frame >= 0 && frame < FrameSpeeds.Length)
            return baseDuration * FrameSpeeds[frame];

        return baseDuration;
    }

    /// <summary>
    /// Sprite key for a given frame index, e.g. "Player/walk-anim-3".
    /// </summary>
    public string FrameKey(int frame) => $"{Key}-{frame}";
}
