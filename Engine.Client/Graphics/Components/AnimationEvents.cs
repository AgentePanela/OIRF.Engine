using Engine.Shared.GameObjects;

namespace Engine.Client.Graphics;

/// <summary>
/// Raised when AnimationSystem.SetAnimation (or SetLayerAnimation) switches an entity to a
/// (different) animation.
/// </summary>
public sealed class AnimationStartedEvent : EntityEvent
{
    public string Key { get; }

    /// <summary>
    /// Id of the SpriteLayer this animation drives, or null for the base sprite animation.
    /// </summary>
    public string? LayerId { get; }

    public AnimationStartedEvent(string key, string? layerId = null)
    {
        Key = key;
        LayerId = layerId;
    }
}

/// <summary>
/// Raised when a non-looping animation reaches its last frame and stops (Playing becomes false).
/// </summary>
public sealed class AnimationFinishedEvent : EntityEvent
{
    public string Key { get; }

    /// <inheritdoc cref="AnimationStartedEvent.LayerId"/>
    public string? LayerId { get; }

    public AnimationFinishedEvent(string key, string? layerId = null)
    {
        Key = key;
        LayerId = layerId;
    }
}

/// <summary>
/// Raised when a looping animation wraps back to frame 0.
/// </summary>
public sealed class AnimationLoopedEvent : EntityEvent
{
    public string Key { get; }

    /// <inheritdoc cref="AnimationStartedEvent.LayerId"/>
    public string? LayerId { get; }

    public AnimationLoopedEvent(string key, string? layerId = null)
    {
        Key = key;
        LayerId = layerId;
    }
}

/// <summary>
/// Raised every time playback advances to a new frame. Useful for syncing hitboxes, footstep
/// sounds, particles, etc. to specific frames.
/// </summary>
public sealed class AnimationFrameChangedEvent : EntityEvent
{
    public string Key { get; }
    public int Frame { get; }

    /// <inheritdoc cref="AnimationStartedEvent.LayerId"/>
    public string? LayerId { get; }

    public AnimationFrameChangedEvent(string key, int frame, string? layerId = null)
    {
        Key = key;
        Frame = frame;
        LayerId = layerId;
    }
}
