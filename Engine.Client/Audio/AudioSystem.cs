using System.Collections.Generic;
using Engine.Client.Graphics;
using Engine.Shared.Audio;
using Engine.Shared.Common;
using Engine.Shared.GameObjects;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using MonoSound.Streaming;

namespace Engine.Client.Audio;

/// <inheritdoc cref="SharedAudioSystem"/>

public sealed class AudioSystem : SharedAudioSystem
{
    [Dependency] private readonly IAudioManager _audio = default!;
    [Dependency] private readonly Camera2D _camera = default!;

    // AudioComponent cant hold the live StreamPackage itself since it is shared
    // track here instead
    private readonly Dictionary<EntityUid, StreamPackage> _playing = new();
    private readonly List<EntityUid> _scratchFinished = new();

    private readonly Dictionary<EntityUid, FadeState> _fades = new();
    private readonly List<EntityUid> _scratchFadesDone = new();
    private readonly List<EntityUid> _scratchFadeStops = new();

    // Reused every ApplySpatial call
    private readonly AudioListener _listener = new();
    private readonly AudioEmitter _emitter = new();

    public override void Update(float dt)
    {
        base.Update(dt);

        if (_playing.Count == 0)
            return;

        _scratchFinished.Clear(); // Safety net
        foreach (var (uid, package) in _playing)
        {
            if (package.FinishedStreaming)
                _scratchFinished.Add(uid);
        }

        foreach (var uid in _scratchFinished)
            NotifyFinished(uid);

        if (_playing.Count == 0)
            return;

        UpdateFades(dt);

        var listenerPos = GetListenerPosition();
        foreach (var (uid, comp, transform) in GetEntitiesWithComp<AudioComponent, TransformComponent>())
        {
            if (!comp.Spatial || !_playing.TryGetValue(uid, out var package))
                continue;

            ApplySpatial(package, comp, transform, listenerPos);
        }
    }

    /// <summary>
    /// Ramps a playing entity volume to <paramref name="target"/> over <paramref name="duration"/>
    /// seconds.
    /// </summary>
    public void Fade(EntityUid uid, float target, float duration, bool stopOnComplete = false)
    {
        if (!TryComp<AudioComponent>(uid, out var comp) || !_playing.TryGetValue(uid, out var package))
            return;

        if (duration <= 0f)
        {
            comp.Volume = target;
            if (!comp.Spatial)
                _audio.SetVolume(package, target);

            _fades.Remove(uid);
            if (stopOnComplete)
                Stop(uid);
            return;
        }

        _fades[uid] = new FadeState
        {
            From = comp.Volume,
            To = target,
            Duration = duration,
            Elapsed = 0f,
            StopOnComplete = stopOnComplete,
        };
    }

    private void UpdateFades(float dt)
    {
        if (_fades.Count == 0)
            return;

        _scratchFadesDone.Clear();
        _scratchFadeStops.Clear();

        foreach (var uid in _fades.Keys)
        {
            var fade = _fades[uid];

            if (!TryComp<AudioComponent>(uid, out var comp) || !_playing.TryGetValue(uid, out var package))
            {
                _scratchFadesDone.Add(uid);
                continue;
            }

            fade.Elapsed += dt;
            var t = MathHelper.Clamp(fade.Elapsed / fade.Duration, 0f, 1f);
            var volume = MathHelper.Lerp(fade.From, fade.To, t);

            comp.Volume = volume;
            if (!comp.Spatial)
                _audio.SetVolume(package, volume);

            if (t >= 1f)
            {
                _scratchFadesDone.Add(uid);
                if (fade.StopOnComplete)
                    _scratchFadeStops.Add(uid);
            }
            else
            {
                _fades[uid] = fade;
            }
        }

        foreach (var uid in _scratchFadesDone)
            _fades.Remove(uid);

        foreach (var uid in _scratchFadeStops)
            Stop(uid);
    }

    /// <summary>
    /// Active AudioListenerComponent's position, falling back to the Camera2D singleton if none exists.
    /// </summary>
    public Vector2 GetListenerPosition()
    {
        foreach (var (_, listener, transform) in GetEntitiesWithComp<AudioListenerComponent, TransformComponent>())
        {
            if (listener.Active)
                return transform.Position;
        }

        return _camera.WorldCenter;
    }

    private void ApplySpatial(StreamPackage package, AudioComponent comp, TransformComponent transform, Vector2 listenerPos)
        => ApplySpatial(package, comp, transform, listenerPos.ToVector3());

    private void ApplySpatial(StreamPackage package, AudioComponent comp, TransformComponent transform, Vector3 listenerPos)
    {
        var maxDistance = MathHelper.Max(comp.MaxDistance, 1f);
        var toListener = transform.Position - listenerPos.ToVector2();
        var distance = toListener.Length();
        var attenuation = MathHelper.Clamp(1f - distance / maxDistance, 0f, 1f);
        _audio.SetVolume(package, comp.Volume * attenuation);
        var direction = distance > 0.0001f ? toListener / distance : Vector2.Zero;
        _emitter.Position = new Vector3(direction.X, listenerPos.Z, direction.Y);
        _audio.Apply3D(package, _listener, _emitter);
    }

    protected override bool OnPlay(EntityUid uid, AudioComponent comp)
    {
        if (!_audio.TryPlay(comp.Key, out var package, comp.Volume, comp.Loop, comp.Pitch, comp.Tags))
            return false;

        _playing[uid] = package;
        return true;
    }

    protected override void OnStop(EntityUid uid)
    {
        _fades.Remove(uid);

        if (_playing.Remove(uid, out var package))
            _audio.Stop(package);
    }

    private struct FadeState
    {
        public float From;
        public float To;
        public float Duration;
        public float Elapsed;
        public bool StopOnComplete;
    }
}
