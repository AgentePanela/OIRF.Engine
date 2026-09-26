using System;
using Engine.Shared.GameObjects;
using Engine.Shared.Timing;

namespace Engine.Shared.Graphics;

/// <summary>
/// Picks and controls what an entity plays.
/// </summary>
public abstract class SharedAnimationSystem : EntitySystem
{
    [Dependency] protected readonly IGameTiming Timing = default!;

    public override void Init()
    {
        base.Init();
        SubscribeEvent<AnimationComponent, CompAddedEvent>(OnAnimationAdded);
    }

    // an animation that comes from a prototype starts playing the moment its entity exists
    private void OnAnimationAdded(EntityUid uid, AnimationComponent comp, CompAddedEvent args)
    {
        if (comp.StartTick == GameTick.Zero)
            comp.StartTick = Timing.CurTick;

        foreach (var layer in comp.Layers)
        {
            if (layer.StartTick == GameTick.Zero)
                layer.StartTick = Timing.CurTick;
        }
    }

    /// <summary>
    /// Switches the entity to a different animation, restarting playback from frame 0.
    /// Adds an AnimationComponent if the entity doesn't have one yet.
    /// </summary>
    public virtual bool SetAnimation(EntityUid uid, string key)
    {
        var comp = EnsureComp<AnimationComponent>(uid);
        comp.Key = key;
        comp.Playing = true;
        comp.StartTick = Timing.CurTick;
        comp.PausedTicks = 0;

        Dirty(uid, comp);
        Restarted(uid, null);
        return true;
    }

    /// <summary>
    /// Switches a single sprite layer to a different animation, restarting playback from frame 0.
    /// Adds an AnimationComponent (and the layer's entry) if they don't exist yet.
    /// </summary>
    public virtual bool SetLayerAnimation(EntityUid uid, string layerId, string key)
    {
        var comp = EnsureComp<AnimationComponent>(uid);
        var layer = GetOrAddLayerAnimation(comp, layerId);
        layer.Key = key;
        layer.Playing = true;
        layer.StartTick = Timing.CurTick;
        layer.PausedTicks = 0;

        Dirty(uid, comp);
        Restarted(uid, layerId);
        return true;
    }

    public void Pause(EntityUid uid)
    {
        if (!TryComp<AnimationComponent>(uid, out var comp) || !comp.Playing)
            return;

        comp.PausedTicks = TicksSince(comp.StartTick);
        comp.Playing = false;
        Dirty(uid, comp);
    }

    public void Resume(EntityUid uid)
    {
        if (!TryComp<AnimationComponent>(uid, out var comp) || comp.Playing)
            return;

        comp.StartTick = TickBefore(comp.PausedTicks);
        comp.Playing = true;
        Dirty(uid, comp);
    }

    /// <summary>
    /// Pauses a single layer animation without affecting the base animation or other layers.
    /// </summary>
    public void PauseLayer(EntityUid uid, string layerId)
    {
        if (!TryComp<AnimationComponent>(uid, out var comp))
            return;

        var layer = GetOrAddLayerAnimation(comp, layerId);
        if (!layer.Playing)
            return;

        layer.PausedTicks = TicksSince(layer.StartTick);
        layer.Playing = false;
        Dirty(uid, comp);
    }

    /// <summary>
    /// Resumes a single layer animation without affecting the base animation or other layers.
    /// </summary>
    public void ResumeLayer(EntityUid uid, string layerId)
    {
        if (!TryComp<AnimationComponent>(uid, out var comp))
            return;

        var layer = GetOrAddLayerAnimation(comp, layerId);
        if (layer.Playing)
            return;

        layer.StartTick = TickBefore(layer.PausedTicks);
        layer.Playing = true;
        Dirty(uid, comp);
    }

    /// <summary>
    /// Overrides this entity's animation speed, in frames per second, regardless of what
    /// info.yml says. Pass null to fall back to the info.yml value. <para/>
    /// </summary>
    public void SetSpeed(EntityUid uid, float? speed)
    {
        if (!TryComp<AnimationComponent>(uid, out var comp))
            return;

        comp.SpeedOverride = speed is null ? null : MathF.Max(0f, speed.Value);
        Dirty(uid, comp);
    }

    /// <summary>
    /// Overrides whether the animation loops, regardless of what the animation says.
    /// Pass null to fall back to the original animation value.
    /// </summary>
    public void SetLoop(EntityUid uid, bool? loop)
    {
        if (!TryComp<AnimationComponent>(uid, out var comp))
            return;

        comp.LoopOverride = loop;
        Dirty(uid, comp);
    }

    /// <summary>
    /// Reset all overrides and send the animation back to its first frame.
    /// </summary>
    public void Reset(EntityUid uid)
    {
        if (!TryComp<AnimationComponent>(uid, out var comp))
            return;

        comp.StartTick = Timing.CurTick;
        comp.PausedTicks = 0;
        comp.LoopOverride = null;
        comp.SpeedOverride = null;

        Dirty(uid, comp);
        Restarted(uid, null);
    }

    /// <summary>
    /// Called when an animation is told to start over.
    /// </summary>
    protected virtual void Restarted(EntityUid uid, string? layerId)
    {
    }

    protected uint TicksSince(GameTick start)
        => Timing.CurTick > start ? Timing.CurTick - start : 0;

    private GameTick TickBefore(uint ticks)
        => Timing.CurTick.Value > ticks ? Timing.CurTick - ticks : GameTick.Zero;

    protected static LayerAnimation GetOrAddLayerAnimation(AnimationComponent comp, string layerId)
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
}
