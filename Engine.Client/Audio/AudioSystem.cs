using System.Collections.Generic;
using Engine.Client.Graphics;
using Engine.Shared.Audio;
using Engine.Shared.GameObjects;
using Engine.Shared.IoC;
using Microsoft.Xna.Framework;
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

        var listenerPos = GetListenerPosition();
        foreach (var (uid, comp, transform) in GetEntitiesWithComp<AudioComponent, TransformComponent>())
        {
            if (!comp.Spatial || !_playing.TryGetValue(uid, out var package))
                continue;

            ApplySpatial(package, comp, transform, listenerPos);
        }
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
    {
        var maxDistance = MathHelper.Max(comp.MaxDistance, 1f);
        var toListener = transform.Position - listenerPos;
        var distance = toListener.Length();

        var attenuation = MathHelper.Clamp(1f - distance / maxDistance, 0f, 1f);
        var pan = MathHelper.Clamp(toListener.X / maxDistance, -1f, 1f);

        _audio.SetVolume(package, comp.Volume * attenuation);
        _audio.SetPan(package, pan);
    }

    protected override bool OnPlay(EntityUid uid, AudioComponent comp)
    {
        if (!_audio.TryPlay(comp.Key, out var package, comp.Volume, comp.Loop, comp.Pitch))
            return false;

        _playing[uid] = package;
        return true;
    }

    protected override void OnStop(EntityUid uid)
    {
        if (_playing.Remove(uid, out var package))
            _audio.Stop(package);
    }
}
