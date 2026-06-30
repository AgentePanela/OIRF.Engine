using Engine.Client.Assets;
using Engine.Client.Assets.Animation;
using Engine.Shared.GameObjects;
using System;

namespace Engine.Client.Graphics;

/// <summary>
/// Advances AnimationComponent playback and writes the resolved frame key into the
/// sibling SpriteComponent.Key.
/// </summary>
public sealed class AnimationSystem : EntitySystem
{
    [Dependency] private readonly IAssetManager _assetMan = default!;

    public override void Update(float dt)
    {
        base.Update(dt);

        foreach (var (uid, anim, sprite) in GetEntitiesWithComp<AnimationComponent, SpriteComponent>())
        {
            if (!anim.Playing)
                continue;

            if (!_assetMan.TryGetAnimation(anim.Key, out var def))
            {
                Log.Warn($"Unknown animation key '{anim.Key}' for entity UID {uid}");
                continue;
            }

            anim.Elapsed += dt;
            var frameDuration = def.GetFrameDuration(anim.CurrentFrame, anim.SpeedOverride);
            if (anim.Elapsed < frameDuration)
                continue;

            anim.Elapsed -= frameDuration;
            anim.CurrentFrame++;

            var loop = anim.LoopOverride ?? def.Loop;
            if (anim.CurrentFrame >= def.FrameCount)
            {
                if (loop)
                {
                    anim.CurrentFrame = 0;
                    RaiseEvent(uid, new AnimationLoopedEvent(anim.Key));
                }
                else
                {
                    anim.CurrentFrame = def.FrameCount - 1;
                    anim.Playing = false;
                    sprite.Key = def.FrameKey(anim.CurrentFrame);
                    RaiseEvent(uid, new AnimationFinishedEvent(anim.Key));
                    continue;
                }
            }

            sprite.Key = def.FrameKey(anim.CurrentFrame);
            RaiseEvent(uid, new AnimationFrameChangedEvent(anim.Key, anim.CurrentFrame));
        }
    }

    /// <summary>
    /// Switches the entity to a different animation, restarting playback from frame 0.
    /// Adds an AnimationComponent if the entity doesn't have one yet. Returns false if the key
    /// doesn't match any loaded animation.
    /// </summary>
    public bool SetAnimation(EntityUid uid, string key)
    {
        if (!_assetMan.TryGetAnimation(key, out var def))
            return false;

        var comp = EnsureComp<AnimationComponent>(uid);
        comp.Key = key;
        comp.CurrentFrame = 0;
        comp.Elapsed = 0f;
        comp.Playing = true;

        if (TryComp<SpriteComponent>(uid, out var sprite))
            sprite.Key = def.FrameKey(0);

        RaiseEvent(uid, new AnimationStartedEvent(key));
        return true;
    }

    /// <summary>
    /// Gets the animation definition currently assigned to the entity, or null if it has no
    /// AnimationComponent or its key doesn't match a loaded animation.
    /// </summary>
    public AnimationDef? GetAnimation(EntityUid uid)
    {
        if (!TryComp<AnimationComponent>(uid, out var comp))
            return null;

        _assetMan.TryGetAnimation(comp.Key, out var def);
        return def;
    }

    public void Pause(EntityUid uid)
    {
        if (TryComp<AnimationComponent>(uid, out var comp))
            comp.Playing = false;
    }

    public void Resume(EntityUid uid)
    {
        if (TryComp<AnimationComponent>(uid, out var comp))
            comp.Playing = true;
    }

    /// <summary>
    /// Overrides this entity's animation speed, in frames per second, regardless of what
    /// info.yml says. Pass null to fall back to the info.yml value.
    /// </summary>
    public void SetSpeed(EntityUid uid, float? speed)
    {
        if (TryComp<AnimationComponent>(uid, out var comp))
            comp.SpeedOverride = speed is null ? null : MathF.Max(0f, speed.Value);
    }

    /// <summary>
    /// Overrides whether the animation loops, regardless of what the animation says.
    /// Pass null to fall back to the original animation value.
    /// </summary>
    public void SetLoop(EntityUid uid, bool? loop)
    {
        if (TryComp<AnimationComponent>(uid, out var comp))
            comp.LoopOverride = loop;
    }

    /// <summary>
    /// Reset all overrides and set the current animation frame to zero.
    /// </summary>
    public void Reset(EntityUid uid)
    {
        if (!TryComp<AnimationComponent>(uid, out var comp))
            return;
        comp.CurrentFrame = 0;
        comp.LoopOverride = null;
        comp.SpeedOverride = null;
    }
}
