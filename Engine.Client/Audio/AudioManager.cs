using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using Engine.Client.Assets;
using MonoSound;
using MonoSound.Default;
using MonoSound.Streaming;
using Microsoft.Xna.Framework.Audio;
using System;
using Engine.Shared.Configuration;
using Engine.Shared.IoC;
using Engine.Shared.Assets;
using Engine.Shared.Audio;
using Engine.Shared.Prototypes;
using Engine.Shared.Storage;

namespace Engine.Client.Audio;

public interface IAudioManager
{
    internal void Init();
    internal void Update(float dt);

    public StreamPackage? Play(string file, float volume = 1f, bool loop = false, float pitch = 0f, IEnumerable<ProtoId<AudioTagPrototype>>? tags = null);

    public bool TryPlay(string file, [NotNullWhen(true)] out StreamPackage? audio, float volume = 1f, bool loop = false, float pitch = 0f, IEnumerable<ProtoId<AudioTagPrototype>>? tags = null);

    public bool HasAudio(string audio);

    /// <summary>Stops playback and disposes the stream immediately.</summary>
    public void Stop(StreamPackage package);

    /// <summary>
    /// Live volume update for an already-playing stream. Does not affect master and tag volume.
    /// </summary>
    public void SetVolume(StreamPackage package, float volume);

    /// <summary>
    /// Live stereo pan update for an already-playing stream. -1 = full left, 1 = full right.
    /// </summary>
    public void SetPan(StreamPackage package, float pan);

    /// <summary>
    /// Current volume multiplier for a tag (0..1, default 1).
    /// </summary>
    public float GetTagVolume(ProtoId<AudioTagPrototype> tag);

    public void SetTagVolume(ProtoId<AudioTagPrototype> tag, float volume);

    /// <summary>Every currently-playing stream tagged with "tag".</summary>
    public IEnumerable<StreamPackage> GetPlayingByTag(ProtoId<AudioTagPrototype> tag);
}

internal sealed partial class AudioManager : IAudioManager
{
    /// <summary>
    /// The shared file/metadata manifest (see SharedAudioManager) - already populated by
    /// SharedContentManager.PostInit() by the time this manager's Init() runs.
    /// </summary>
    [Dependency] private readonly SharedAudioManager _registry = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public readonly List<(Stream stream, StreamPackage package, List<ProtoId<AudioTagPrototype>> tags, float baseVolume)> RunningStreams = new();

    // runtime-only volume multiplier per tag (0..1, default 1) - not persisted.
    private readonly Dictionary<string, float> _tagVolumes = new();

    public AudioManager()
        => IoCManager.ResolveDependencies(this);

    void IAudioManager.Init()
    {
        MonoSoundLibrary.Init(GameClient.Instance);

        _cfg.Subs(AudioCvars.MasterVolume, _ => RecomputeVolumes());

        #if DEBUG
        InitHotReload();
        #endif

        GameClient.Instance.Exiting += (_, _) => MonoSoundLibrary.DeInit();
    }

    void IAudioManager.Update(float dt)
    {
        #if DEBUG
        DrainHotReloadQueue();
        #endif

        for (int i = RunningStreams.Count - 1; i >= 0; i--)
        {
            var sound = RunningStreams[i];

            if (sound.package.FinishedStreaming)
            {
                sound.package.Dispose();
                sound.stream.Dispose();
                RunningStreams.RemoveAt(i);
            }
        }
    }

    public StreamPackage? Play(string file, float volume = 1f, bool loop = false, float pitch = 0f, IEnumerable<ProtoId<AudioTagPrototype>>? tags = null)
    {
        TryPlay(file, out var audio, volume, loop, pitch, tags);
        return audio;
    }

    public bool TryPlay(string file, [NotNullWhen(true)] out StreamPackage? audio, float volume = 1f, bool loop = false, float pitch = 0f, IEnumerable<ProtoId<AudioTagPrototype>>? tags = null)
    {
        audio = default;
        if (!HasAudio(file))
            return false;

        var tagList = tags is null ? new List<ProtoId<AudioTagPrototype>>() : new List<ProtoId<AudioTagPrototype>>(tags);

        audio = GetPackage(file, tagList, volume);
        if (audio is null)
            return false;

        audio.PlayingSound.Volume = volume * GetBusMultiplier(tagList);
        audio.PlayingSound.Pitch = pitch;
        audio.IsLooping = loop;
        audio.Play();
        return true;
    }

    /// <summary>
    /// Stops playback and immediately reaps the RunningStreams entry - a manual stop isn't
    /// guaranteed to flip FinishedStreaming, so Update()'s GC pass can't be relied on here.
    /// </summary>
    public void Stop(StreamPackage package)
    {
        package.Stop();

        for (int i = RunningStreams.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(RunningStreams[i].package, package))
                continue;

            var entry = RunningStreams[i];
            RunningStreams.RemoveAt(i);
            entry.package.Dispose();
            entry.stream.Dispose();
            break;
        }
    }

    public bool HasAudio(string file)
        => _registry.HasAudio(file);

    public void SetVolume(StreamPackage package, float volume)
    {
        var multiplier = 1f;
        foreach (var entry in RunningStreams)
        {
            if (!ReferenceEquals(entry.package, package))
                continue;

            multiplier = GetBusMultiplier(entry.tags);
            break;
        }

        package.PlayingSound.Volume = volume * multiplier;
    }

    public void SetPan(StreamPackage package, float pan)
        => package.PlayingSound.Pan = pan;

    public float GetTagVolume(ProtoId<AudioTagPrototype> tag)
    {
        AssertValidTag(tag);
        return _tagVolumes.TryGetValue(tag.Id, out var volume) ? volume : 1f;
    }

    public void SetTagVolume(ProtoId<AudioTagPrototype> tag, float volume)
    {
        AssertValidTag(tag);
        _tagVolumes[tag.Id] = volume;
        RecomputeVolumes();
    }

    private void AssertValidTag(ProtoId<AudioTagPrototype> tag)
    {
        #if DEBUG
        if (!_proto.HasIndex(tag))
            throw new UnknowPrototypeException($"Unknow audioTag prototype {tag.Id}.");
        #endif
    }

    public IEnumerable<StreamPackage> GetPlayingByTag(ProtoId<AudioTagPrototype> tag)
    {
        AssertValidTag(tag);

        foreach (var entry in RunningStreams)
        {
            if (entry.tags.Contains(tag))
                yield return entry.package;
        }
    }

    private float GetBusMultiplier(List<ProtoId<AudioTagPrototype>> tags)
    {
        var multiplier = _cfg.Get(AudioCvars.MasterVolume);
        foreach (var tag in tags)
            multiplier *= GetTagVolume(tag);

        return multiplier;
    }

    /// <summary>Reapplies Master/tag volume to every currently-playing stream - called when a volume CVar changes live.</summary>
    private void RecomputeVolumes()
    {
        foreach (var entry in RunningStreams)
            entry.package.PlayingSound.Volume = entry.baseVolume * GetBusMultiplier(entry.tags);
    }

    private StreamPackage? GetPackage(string relative, List<ProtoId<AudioTagPrototype>> tags, float baseVolume)
    {
        if (!_registry.TryGetPath(relative, out var fullPath))
            return null;

        var stream = FileSystem.OpenRead(fullPath);
        stream.Position = 0;
        var sound = StreamLoader.GetStreamedSound(stream, AudioType.OGG, false);
        RunningStreams.Add((stream, sound, tags, baseVolume));
        return sound;
    }
}
