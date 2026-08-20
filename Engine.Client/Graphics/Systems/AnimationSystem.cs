using Engine.Client.Assets;
using Engine.Client.Assets.Animation;
using Engine.Shared.GameObjects;
using System;

namespace Engine.Client.Graphics;

/// <summary>
/// Advances AnimationComponent playback and writes the resolved frame key into the sibling
/// SpriteComponent.Key (base animation) and/or individual SpriteComponent.Layers entries
/// (per-layer animations, see AnimationComponent.Layers).
/// </summary>
public sealed class AnimationSystem : EntitySystem
{
    [Dependency] private readonly IAssetManager _assetMan = default!;
    [Dependency] private readonly SpriteSystem _spriteSys = default!;

    public override void Update(float dt)
    {
        base.Update(dt);

        foreach (var (uid, anim, sprite) in GetEntitiesWithComp<AnimationComponent, SpriteComponent>())
        {
            if (anim.Playing && !string.IsNullOrEmpty(anim.Key))
                UpdateBase(uid, anim, sprite, dt);

            if (anim.Layers.Count == 0)
                continue;

            foreach (var layerAnim in anim.Layers)
                UpdateLayer(uid, sprite, layerAnim, dt);
        }
    }

    private void UpdateBase(EntityUid uid, AnimationComponent anim, SpriteComponent sprite, float dt)
    {
        if (!_assetMan.TryGetAnimation(anim.Key, out var def))
        {
            Log.Warn($"Unknown animation key '{anim.Key}' for entity UID {uid}");
            return;
        }

        var frame = anim.CurrentFrame;
        var elapsed = anim.Elapsed;
        var playing = anim.Playing;
        if (!Advance(def, dt, anim.SpeedOverride, anim.LoopOverride, ref frame, ref elapsed, ref playing, out var looped, out var finished))
        {
            anim.Elapsed = elapsed;
            return;
        }

        anim.CurrentFrame = frame;
        anim.Elapsed = elapsed;
        anim.Playing = playing;
        sprite.Key = def.FrameKey(frame);

        if (looped)
            RaiseEvent(uid, new AnimationLoopedEvent(anim.Key));
        else if (finished)
            RaiseEvent(uid, new AnimationFinishedEvent(anim.Key));
        else
            RaiseEvent(uid, new AnimationFrameChangedEvent(anim.Key, frame));
    }

    private void UpdateLayer(EntityUid uid, SpriteComponent sprite, LayerAnimation layerAnim, float dt)
    {
        if (!layerAnim.Playing || string.IsNullOrEmpty(layerAnim.Key))
            return;

        var layer = _spriteSys.GetLayer(sprite, layerAnim.LayerId);
        if (layer is null)
            return;

        if (!_assetMan.TryGetAnimation(layerAnim.Key, out var def))
        {
            Log.Warn($"Unknown animation key '{layerAnim.Key}' for entity UID {uid}, layer '{layerAnim.LayerId}'");
            return;
        }

        var frame = layerAnim.CurrentFrame;
        var elapsed = layerAnim.Elapsed;
        var playing = layerAnim.Playing;
        if (!Advance(def, dt, layerAnim.SpeedOverride, layerAnim.LoopOverride, ref frame, ref elapsed, ref playing, out var looped, out var finished))
        {
            layerAnim.Elapsed = elapsed;
            return;
        }

        layerAnim.CurrentFrame = frame;
        layerAnim.Elapsed = elapsed;
        layerAnim.Playing = playing;
        layer.Key = def.FrameKey(frame);

        if (looped)
            RaiseEvent(uid, new AnimationLoopedEvent(layerAnim.Key, layerAnim.LayerId));
        else if (finished)
            RaiseEvent(uid, new AnimationFinishedEvent(layerAnim.Key, layerAnim.LayerId));
        else
            RaiseEvent(uid, new AnimationFrameChangedEvent(layerAnim.Key, frame, layerAnim.LayerId));
    }

    /// <summary>
    /// Shared frame-advance logic for both the base animation and per-layer animations.
    /// Returns false (with elapsed still updated) if no frame boundary was crossed this tick.
    /// </summary>
    private static bool Advance(AnimationDef def, float dt, float? speedOverride, bool? loopOverride,
        ref int frame, ref float elapsed, ref bool playing, out bool looped, out bool finished)
    {
        looped = false;
        finished = false;

        elapsed += dt;
        var frameDuration = def.GetFrameDuration(frame, speedOverride);
        if (elapsed < frameDuration)
            return false;

        elapsed -= frameDuration;
        frame++;

        var loop = loopOverride ?? def.Loop;
        if (frame >= def.FrameCount)
        {
            if (loop)
            {
                frame = 0;
                looped = true;
            }
            else
            {
                frame = def.FrameCount - 1;
                playing = false;
                finished = true;
            }
        }

        return true;
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
    /// Switches a single sprite layer to a different animation, restarting playback from frame 0.
    /// Adds an AnimationComponent (and the layer's entry) if they don't exist yet. Returns false
    /// if the key doesn't match any loaded animation.
    /// </summary>
    public bool SetLayerAnimation(EntityUid uid, string layerId, string key)
    {
        if (!_assetMan.TryGetAnimation(key, out var def))
            return false;

        var comp = EnsureComp<AnimationComponent>(uid);
        var layerAnim = GetOrAddLayerAnimation(comp, layerId);
        layerAnim.Key = key;
        layerAnim.CurrentFrame = 0;
        layerAnim.Elapsed = 0f;
        layerAnim.Playing = true;

        if (TryComp<SpriteComponent>(uid, out var sprite))
        {
            var layer = _spriteSys.GetLayer(sprite, layerId);
            if (layer is not null)
                layer.Key = def.FrameKey(0);
        }

        RaiseEvent(uid, new AnimationStartedEvent(key, layerId));
        return true;
    }

    private static LayerAnimation GetOrAddLayerAnimation(AnimationComponent comp, string layerId)
    {
        foreach (var layerAnim in comp.Layers)
        {
            if (layerAnim.LayerId == layerId)
                return layerAnim;
        }

        var created = new LayerAnimation { LayerId = layerId };
        comp.Layers.Add(created);
        return created;
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
    /// Pauses a single layer's animation without affecting the base animation or other layers.
    /// </summary>
    public void PauseLayer(EntityUid uid, string layerId)
    {
        if (TryComp<AnimationComponent>(uid, out var comp))
            GetOrAddLayerAnimation(comp, layerId).Playing = false;
    }

    /// <summary>
    /// Resumes a single layer's animation without affecting the base animation or other layers.
    /// </summary>
    public void ResumeLayer(EntityUid uid, string layerId)
    {
        if (TryComp<AnimationComponent>(uid, out var comp))
            GetOrAddLayerAnimation(comp, layerId).Playing = true;
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
