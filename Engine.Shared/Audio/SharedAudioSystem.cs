using System;
using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Engine.Shared.Prototypes;
using Microsoft.Xna.Framework;

namespace Engine.Shared.Audio;

/// <summary>
/// Handle AudioComponent lifecycle and timing: autoplay, looping, and AudioFinishedEvent.
/// </summary>
public abstract class SharedAudioSystem : EntitySystem
{
    [Dependency] protected readonly IPrototypeManager _proto = default!;
    [Dependency] protected readonly SharedAudioManager _registry = default!;

    private readonly List<EntityUid> _scratchFinished = new();

    // entities created by PlaySound() - self-delete once their sound is truly done.
    private readonly HashSet<EntityUid> _transientSounds = new();

    public override void Init()
    {
        base.Init();

        SubscribeEvent<AudioComponent, CompAddedEvent>(OnAudioAdded);
        SubscribeEvent<AudioComponent, CompRemovedEvent>(OnAudioRemoved);
    }

    public override void Update(float dt)
    {
        base.Update(dt);

        _scratchFinished.Clear();
        foreach (var (uid, comp) in GetEntitiesWithComp<AudioComponent>())
        {
            if (comp.Elapsed is not { } elapsed)
                continue;

            var next = elapsed + dt;

            if (!_registry.TryGetMetadata(comp.Key, out var metadata) || metadata.Duration <= TimeSpan.Zero)
            {
                comp.Elapsed = next; // unknown duration - keep the clock running, never auto-finishes here
                continue;
            }

            var duration = (float)metadata.Duration.TotalSeconds;
            if (next < duration)
            {
                comp.Elapsed = next;
                continue;
            }

            if (comp.Loop)
                comp.Elapsed = next % duration;
            else
                _scratchFinished.Add(uid);
        }

        foreach (var uid in _scratchFinished)
        {
            StopInternal(uid, raise: true);
            DeleteTransient(uid);
        }
    }

    /// <summary>
    /// Spawns a throwaway entity that plays the audio once and deletes itself when done
    /// </summary>
    public EntityUid PlaySound(string key, Vector2? position = null, float volume = 1f, float pitch = 0f,
        bool spatial = false, float maxDistance = 1000f, IEnumerable<ProtoId<AudioTagPrototype>>? tags = null)
    {
        var uid = CreateEmptyEntity("Sound");

        var transform = AddComp<TransformComponent>(uid);
        if (position is not null)
            transform.Position = position.Value;

        var audio = AddComp<AudioComponent>(uid);
        audio.Key = key;
        audio.Volume = volume;
        audio.Pitch = pitch;
        audio.Loop = false; // transient sounds never loop - use a real entity + AudioComponent for that
        audio.AutoPlay = true;
        audio.Spatial = spatial;
        audio.MaxDistance = maxDistance;
        if (tags is not null)
            audio.Tags = new(tags);

        _transientSounds.Add(uid);
        return uid;
    }

    private void OnAudioAdded(EntityUid uid, AudioComponent comp, CompAddedEvent ev)
    {
#if DEBUG
        foreach (var tag in comp.Tags)
            AssertInvalidTag(tag, uid);
#endif

        if (!comp.AutoPlay)
            return;

        if (!Play(uid, comp))
            DeleteTransient(uid); // never started - nothing will raise AudioFinishedEvent to clean this up later
    }

    private void OnAudioRemoved(EntityUid uid, AudioComponent comp, CompRemovedEvent ev)
    {
        StopInternal(uid, raise: false);
        _transientSounds.Remove(uid); // entity's already being removed by whatever triggered this, don't delete it again
    }

#if DEBUG
    private void AssertInvalidTag(string id, EntityUid uid)
    {
        if (!_proto.HasIndex<AudioTagPrototype>(id))
            throw new UnknowPrototypeException($"Unknow audioTag prototype {id} in entity {uid}!");
    }
#endif

    /// <summary>Starts (or restarts) playback for this entity's AudioComponent.</summary>
    public bool Play(EntityUid uid, AudioComponent comp)
    {
        StopInternal(uid, raise: false); // reset if already playing (restart) - doesn't touch transient bookkeeping

        if (!OnPlay(uid, comp))
            return false;

        comp.Elapsed = 0f;
        return true;
    }

    /// <summary>Stops playback for this entity, if any is currently tracked. Does not raise AudioFinishedEvent.</summary>
    public void Stop(EntityUid uid)
    {
        StopInternal(uid, raise: false);
        DeleteTransient(uid);
    }

    /// <summary>
    /// Call from a subclass when it independently detects a clip has genuinely finished (e.g.
    /// the real playback backend reports done) before the shared elapsed-time estimate catches
    /// up - raises AudioFinishedEvent and cleans up a transient PlaySound entity, same as a
    /// natural finish detected by Update() itself.
    /// </summary>
    protected void NotifyFinished(EntityUid uid)
    {
        StopInternal(uid, raise: true);
        DeleteTransient(uid);
    }

    /// <param name="raise">True when this is a natural end-of-clip (raises AudioFinishedEvent), false for an explicit stop/removal/restart.</param>
    private void StopInternal(EntityUid uid, bool raise)
    {
        if (!TryComp<AudioComponent>(uid, out var comp) || comp.Elapsed is null)
            return;

        comp.Elapsed = null;
        OnStop(uid);

        if (raise)
            RaiseEvent(uid, new AudioFinishedEvent());
    }

    private void DeleteTransient(EntityUid uid)
    {
        if (!_transientSounds.Remove(uid))
            return;

        if (HasEntity(uid, out var ent) && !ent.Deleting)
            DeleteEntity(uid);
    }

    public bool IsPlaying(EntityUid uid)
        => TryComp<AudioComponent>(uid, out var comp) && comp.Elapsed is not null;

    /// <summary>Override to layer real playback on top. Return false to abort - the entity won't be considered playing.</summary>
    protected virtual bool OnPlay(EntityUid uid, AudioComponent comp) => true;

    /// <summary>Override to release any resource acquired in OnPlay.</summary>
    protected virtual void OnStop(EntityUid uid) { }
}

public sealed class AudioFinishedEvent : EntityEvent
{
}
