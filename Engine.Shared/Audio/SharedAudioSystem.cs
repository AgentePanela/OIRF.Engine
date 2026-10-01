using System;
using System.Collections.Generic;
using Engine.Shared.GameObjects;
using Engine.Shared.Prototypes;
using Engine.Shared.Timing;
using Microsoft.Xna.Framework;

namespace Engine.Shared.Audio;

/// <summary>
/// Handle AudioComponent lifecycle and timing: autoplay, looping, and AudioFinishedEvent.
/// </summary>
public abstract class SharedAudioSystem : EntitySystem
{
    [Dependency] protected readonly IPrototypeManager _proto = default!;
    [Dependency] protected readonly SharedAudioManifest _registry = default!;
    [Dependency] protected readonly IGameTiming Timing = default!;

    private const float MinStartOffset = 0.25f;

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
    /// Plays a sound once, the same for everyone that gets it wherever they are;
    /// </summary>
    public EntityUid PlaySound(string key, float volume = 1f, float pitch = 0f,
        IEnumerable<ProtoId<AudioTagPrototype>>? tags = null, IEntityScene? scene = null)
        => SpawnSound(key, null, volume, pitch, spatial: false, maxDistance: 0f, tags, scene);

    /// <inheritdoc cref="PlaySound(string, float, float, IEnumerable{ProtoId{AudioTagPrototype}}?, IEntityScene?)"/>
    /// <returns>Null when <paramref name="sound"/> resolves to nothing.</returns>
    public EntityUid? PlaySound(SoundSpecifier? sound, float volume = 1f, float pitch = 0f,
        IEnumerable<ProtoId<AudioTagPrototype>>? tags = null, IEntityScene? scene = null)
        => sound is not null && ResolveSound(sound) is { } key
            ? PlaySound(key, volume * sound.Volume, pitch + sound.Pitch, tags, scene)
            : null;

    /// <summary>
    /// Plays a sound once at <paramref name="position"/.
    /// </summary>
    public EntityUid PlayAtPosition(string key, Vector2 position, float volume = 1f, float pitch = 0f,
        float maxDistance = 1000f, IEnumerable<ProtoId<AudioTagPrototype>>? tags = null, IEntityScene? scene = null)
        => SpawnSound(key, position, volume, pitch, spatial: true, maxDistance, tags, scene);

    /// <inheritdoc cref="PlayAtPosition(string, Vector2, float, float, float, IEnumerable{ProtoId{AudioTagPrototype}}?, IEntityScene?)"/>
    /// <returns>Null when <paramref name="sound"/> resolves to nothing.</returns>
    public EntityUid? PlayAtPosition(SoundSpecifier? sound, Vector2 position, float volume = 1f, float pitch = 0f,
        float maxDistance = 1000f, IEnumerable<ProtoId<AudioTagPrototype>>? tags = null, IEntityScene? scene = null)
        => sound is not null && ResolveSound(sound) is { } key
            ? PlayAtPosition(key, position, volume * sound.Volume, pitch + sound.Pitch, maxDistance, tags, scene)
            : null;

    private EntityUid SpawnSound(string key, Vector2? position, float volume, float pitch, bool spatial,
        float maxDistance, IEnumerable<ProtoId<AudioTagPrototype>>? tags, IEntityScene? scene)
    {
        var uid = CreateEmptyEntity("Sound", scene);

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

        OnSoundSpawned(uid, spatial, maxDistance, scene);

        return uid;
    }

    protected virtual void OnSoundSpawned(EntityUid uid, bool spatial, float maxDistance, IEntityScene? scene) { }

    public string? ResolveSound(SoundSpecifier sound)
    {
        switch (sound)
        {
            case SoundPathSpecifier path:
                return path.Path.Length == 0 ? null : path.Path;

            case SoundCollectionSpecifier collection:
                if (!_proto.TryIndex(collection.Collection, out var proto) || proto.Files.Count == 0)
                {
                    Log.Warn($"Sound collection '{collection.Collection}' doesn't exist or has no files.");
                    return null;
                }

                return proto.Files[Random.Shared.Next(proto.Files.Count)];

            default:
                return null;
        }
    }

    private void OnAudioAdded(EntityUid uid, AudioComponent comp, CompAddedEvent ev)
    {
#if DEBUG
        foreach (var tag in comp.Tags)
            AssertInvalidTag(tag, uid);
#endif

        if (!comp.AutoPlay)
            return;

        // already stamped means it came in a state, and the sound has been going for a while on the server
        var startTick = comp.StartTick != 0 ? comp.StartTick : Timing.CurTick.Value;

        if (!Start(uid, comp, startTick))
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
        => Start(uid, comp, Timing.CurTick.Value);

    private bool Start(EntityUid uid, AudioComponent comp, uint startTick)
    {
        StopInternal(uid, raise: false); // reset if already playing (restart) - doesn't touch transient bookkeeping

        var ticks = Timing.CurTick.Value > startTick ? Timing.CurTick.Value - startTick : 0;
        var offset = ticks * Timing.TickPeriod;

        if (offset < MinStartOffset)
        {
            offset = 0f;
        }
        else if (_registry.TryGetMetadata(comp.Key, out var metadata) && metadata.Duration > TimeSpan.Zero)
        {
            var duration = (float)metadata.Duration.TotalSeconds;
            if (comp.Loop)
                offset %= duration;
            else if (offset >= duration)
                return false; // it already ended for everyone else
        }

        if (comp.StartTick != startTick)
            comp.StartTick = startTick;

        if (!OnPlay(uid, comp, offset))
            return false;

        comp.Elapsed = offset;
        return true;
    }

    /// <summary>
    /// Moves every playing sound's StartTick to where it would be if the whole sound had run at
    /// <paramref name="tickRate"/>.
    /// </summary>
    protected void RestampStartTicks(float tickRate)
    {
        if (tickRate <= 0f)
            return;

        foreach (var (_, comp) in GetEntitiesWithComp<AudioComponent>())
        {
            if (comp.Elapsed is not { } elapsed)
                continue;

            var ticks = (uint)MathF.Round(elapsed * tickRate);
            var startTick = Timing.CurTick.Value > ticks ? Timing.CurTick.Value - ticks : 0;

            if (comp.StartTick != startTick)
                comp.StartTick = startTick;
        }
    }

    /// <summary>Stops playback for this entity, if any is currently tracked. Does not raise AudioFinishedEvent.</summary>
    public void Stop(EntityUid uid)
    {
        StopInternal(uid, raise: false);
        DeleteTransient(uid);
    }

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

    /// <summary>
    /// Every entity currently playing an AudioComponent tagged with "tag".
    /// </summary>
    public IEnumerable<EntityUid> GetPlayingByTag(ProtoId<AudioTagPrototype> tag)
    {
#if DEBUG
        if (!_proto.HasIndex(tag))
            throw new UnknowPrototypeException($"Unknow audioTag prototype {tag.Id}.");
#endif

        foreach (var (uid, comp) in GetEntitiesWithComp<AudioComponent>())
        {
            if (comp.Elapsed is not null && comp.Tags.Contains(tag))
                yield return uid;
        }
    }

    /// <summary>Override to layer real playback on top. Return false to abort - the entity won't be considered playing.</summary>
    protected virtual bool OnPlay(EntityUid uid, AudioComponent comp, float offset) => true;

    /// <summary>Override to release any resource acquired in OnPlay.</summary>
    protected virtual void OnStop(EntityUid uid) { }
}

public sealed class AudioFinishedEvent : EntityEvent
{
}
