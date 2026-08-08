using System.Collections.Generic;
using Engine.Shared.Audio;
using Engine.Shared.GameObjects;
using Engine.Shared.IoC;
using MonoSound.Streaming;

namespace Engine.Client.Audio;

/// <inheritdoc cref="SharedAudioSystem"/>

public sealed class AudioSystem : SharedAudioSystem
{
    [Dependency] private readonly IAudioManager _audio = default!;

    // AudioComponent cant hold the live StreamPackage itself since it is shared
    // track here instead
    private readonly Dictionary<EntityUid, StreamPackage> _playing = new();
    private readonly List<EntityUid> _scratchFinished = new();

    public override void Update(float dt)
    {
        base.Update(dt);

        _scratchFinished.Clear(); // Safety net
        foreach (var (uid, package) in _playing)
        {
            if (package.FinishedStreaming)
                _scratchFinished.Add(uid);
        }

        foreach (var uid in _scratchFinished)
            NotifyFinished(uid);
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
