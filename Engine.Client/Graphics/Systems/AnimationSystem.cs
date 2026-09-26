using System;
using System.Collections.Generic;
using Engine.Client.Assets;
using Engine.Client.Assets.Animation;
using Engine.Shared.GameObjects;
using Engine.Shared.Graphics;
using Engine.Shared.Timing;

namespace Engine.Client.Graphics;

/// <summary>
/// Plays AnimationComponents and draws the resolved frame through the SpriteSystem, over the base sprite
/// (SpriteComponent.Key) and/or individual SpriteComponent.Layers entries.
/// </summary>
public sealed class AnimationSystem : SharedAnimationSystem
{
    [Dependency] private readonly IAssetManager _assetMan = default!;
    [Dependency] private readonly SpriteSystem _spriteSys = default!;

    private readonly Dictionary<EntityUid, EntityPlayback> _playback = new();
    private readonly List<string> _staleLayers = new();

    private sealed class EntityPlayback
    {
        public readonly Playback Base = new();
        public readonly Dictionary<string, Playback> Layers = new();
    }

    private sealed class Playback
    {
        public bool Synced;
        public string SyncedKey = string.Empty;
        public GameTick SyncedStart;

        public int Frame;
        public float Elapsed;
        public bool Finished;
    }

    public override void Init()
    {
        base.Init();
        SubscribeEvent<AnimationComponent, CompRemovedEvent>(OnAnimationRemoved);
    }

    private void OnAnimationRemoved(EntityUid uid, AnimationComponent comp, CompRemovedEvent args)
    {
        if (!_playback.Remove(uid, out var playback))
            return;

        _spriteSys.SetDrawKey(uid, null, null);
        foreach (var layerId in playback.Layers.Keys)
            _spriteSys.SetDrawKey(uid, layerId, null);
    }

    public override void Update(float dt)
    {
        base.Update(dt);

        foreach (var (uid, anim, sprite) in GetEntitiesWithComp<AnimationComponent, SpriteComponent>())
        {
            var playback = GetPlayback(uid);

            Step(uid, null, anim.Key, anim.Playing, anim.StartTick, anim.PausedTicks,
                anim.SpeedOverride, anim.LoopOverride, playback.Base, dt);

            foreach (var layerAnim in anim.Layers)
            {
                // wait for the layer to exist - it may come in a later state than its animation
                if (_spriteSys.GetLayer(sprite, layerAnim.LayerId) is null)
                    continue;

                Step(uid, layerAnim.LayerId, layerAnim.Key, layerAnim.Playing, layerAnim.StartTick, layerAnim.PausedTicks,
                    layerAnim.SpeedOverride, layerAnim.LoopOverride, GetLayerPlayback(playback, layerAnim.LayerId), dt);
            }

            DropStaleLayers(uid, anim, playback);
        }
    }

    private void Step(EntityUid uid, string? layerId, string key, bool playing, GameTick startTick, uint pausedTicks,
        float? speedOverride, bool? loopOverride, Playback playback, float dt)
    {
        if (string.IsNullOrEmpty(key))
        {
            if (playback.Synced)
            {
                playback.Synced = false;
                _spriteSys.SetDrawKey(uid, layerId, null);
            }
            return;
        }

        var keyChanged = !playback.Synced || playback.SyncedKey != key;
        if (keyChanged || playback.SyncedStart != startTick)
        {
            playback.Synced = true;
            playback.SyncedKey = key;
            playback.SyncedStart = startTick;

            if (!_assetMan.TryGetAnimation(key, out var syncDef))
            {
                if (layerId is null)
                    Log.Warn($"Unknown animation key '{key}' for entity UID {uid}");
                else
                    Log.Warn($"Unknown animation key '{key}' for entity UID {uid}, layer '{layerId}'");

                _spriteSys.SetDrawKey(uid, layerId, null);
                return;
            }

            // a paused animation stays where it was paused, not where the current tick would put it
            var ticks = playing ? TicksSince(startTick) : pausedTicks;
            Seek(syncDef, ticks * Timing.TickPeriod, speedOverride, loopOverride, playback);
            _spriteSys.SetDrawKey(uid, layerId, syncDef.FrameKey(playback.Frame));

            if (keyChanged)
                RaiseEvent(uid, new AnimationStartedEvent(key, layerId));

            return;
        }

        if (!playing || playback.Finished || !_assetMan.TryGetAnimation(key, out var def))
            return;

        if (!Advance(def, dt, speedOverride, loopOverride, playback, out var looped, out var finished))
            return;

        _spriteSys.SetDrawKey(uid, layerId, def.FrameKey(playback.Frame));

        if (looped)
            RaiseEvent(uid, new AnimationLoopedEvent(key, layerId));
        else if (finished)
            RaiseEvent(uid, new AnimationFinishedEvent(key, layerId));
        else
            RaiseEvent(uid, new AnimationFrameChangedEvent(key, playback.Frame, layerId));
    }

    /// <summary>
    /// Puts <paramref name="playback"/> where an animation that has been running for <paramref name="seconds"/>
    /// would be.
    /// </summary>
    private static void Seek(AnimationDef def, float seconds, float? speedOverride, bool? loopOverride, Playback playback)
    {
        var loop = loopOverride ?? def.Loop;

        if (loop)
        {
            var cycle = 0f;
            for (var f = 0; f < def.FrameCount; f++)
                cycle += def.GetFrameDuration(f, speedOverride);

            if (cycle > 0f && float.IsFinite(cycle))
                seconds %= cycle;
        }

        var frame = 0;
        while (frame < def.FrameCount)
        {
            var duration = def.GetFrameDuration(frame, speedOverride);
            if (seconds < duration)
                break;

            seconds -= duration;
            frame++;
        }

        playback.Finished = false;
        if (frame >= def.FrameCount)
        {
            // only a finished one-shot gets here - a looping one was wrapped above
            frame = Math.Max(0, def.FrameCount - 1);
            seconds = 0f;
            playback.Finished = !loop;
        }

        playback.Frame = frame;
        playback.Elapsed = seconds;
    }

    /// <summary>
    /// Returns false (with elapsed still updated) if no frame boundary was crossed this tick.
    /// </summary>
    private static bool Advance(AnimationDef def, float dt, float? speedOverride, bool? loopOverride,
        Playback playback, out bool looped, out bool finished)
    {
        looped = false;
        finished = false;

        playback.Elapsed += dt;
        var frameDuration = def.GetFrameDuration(playback.Frame, speedOverride);
        if (playback.Elapsed < frameDuration)
            return false;

        playback.Elapsed -= frameDuration;
        playback.Frame++;

        var loop = loopOverride ?? def.Loop;
        if (playback.Frame >= def.FrameCount)
        {
            if (loop)
            {
                playback.Frame = 0;
                looped = true;
            }
            else
            {
                playback.Frame = def.FrameCount - 1;
                playback.Finished = true;
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
    public override bool SetAnimation(EntityUid uid, string key)
        => _assetMan.TryGetAnimation(key, out _) && base.SetAnimation(uid, key);

    /// <summary>
    /// Switches a single sprite layer to a different animation, restarting playback from frame 0.
    /// Adds an AnimationComponent (and the layer's entry) if they don't exist yet. Returns false
    /// if the key doesn't match any loaded animation.
    /// </summary>
    public override bool SetLayerAnimation(EntityUid uid, string layerId, string key)
        => _assetMan.TryGetAnimation(key, out _) && base.SetLayerAnimation(uid, layerId, key);

    protected override void Restarted(EntityUid uid, string? layerId)
    {
        if (!_playback.TryGetValue(uid, out var playback))
            return;

        if (layerId is null)
            playback.Base.Synced = false;
        else if (playback.Layers.TryGetValue(layerId, out var layer))
            layer.Synced = false;
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

    /// <summary>
    /// The frame the base animation of an entity is showing right now.
    /// </summary>
    public int GetCurrentFrame(EntityUid uid)
        => _playback.TryGetValue(uid, out var playback) ? playback.Base.Frame : 0;

    private EntityPlayback GetPlayback(EntityUid uid)
    {
        if (!_playback.TryGetValue(uid, out var playback))
            _playback[uid] = playback = new EntityPlayback();

        return playback;
    }

    private static Playback GetLayerPlayback(EntityPlayback playback, string layerId)
    {
        if (!playback.Layers.TryGetValue(layerId, out var layer))
            playback.Layers[layerId] = layer = new Playback();

        return layer;
    }

    /// <summary>
    /// a layer animation that went away has to give the layer its own key back
    /// </summary>
    /// <param name="uid"></param>
    /// <param name="anim"></param>
    /// <param name="playback"></param>
    private void DropStaleLayers(EntityUid uid, AnimationComponent anim, EntityPlayback playback)
    {
        if (playback.Layers.Count == 0)
            return;

        foreach (var layerId in playback.Layers.Keys)
        {
            if (!anim.Layers.Exists(l => l.LayerId == layerId))
                _staleLayers.Add(layerId);
        }

        foreach (var layerId in _staleLayers)
        {
            playback.Layers.Remove(layerId);
            _spriteSys.SetDrawKey(uid, layerId, null);
        }

        _staleLayers.Clear();
    }
}
