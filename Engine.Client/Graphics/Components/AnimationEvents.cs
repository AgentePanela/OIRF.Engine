using Engine.Shared.GameObjects;

namespace Engine.Client.Graphics;

/// <summary>
/// Raised when AnimationSystem.SetAnimation switches an entity to a (different) animation.
/// </summary>
public sealed class AnimationStartedEvent : EntityEvent
{
    public string Key { get; }

    public AnimationStartedEvent(string key)
    {
        Key = key;
    }
}

/// <summary>
/// Raised when a non-looping animation reaches its last frame and stops (AnimationComponent.Playing becomes false).
/// </summary>
public sealed class AnimationFinishedEvent : EntityEvent
{
    public string Key { get; }

    public AnimationFinishedEvent(string key)
    {
        Key = key;
    }
}

/// <summary>
/// Raised when a looping animation wraps back to frame 0.
/// </summary>
public sealed class AnimationLoopedEvent : EntityEvent
{
    public string Key { get; }

    public AnimationLoopedEvent(string key)
    {
        Key = key;
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

    public AnimationFrameChangedEvent(string key, int frame)
    {
        Key = key;
        Frame = frame;
    }
}
