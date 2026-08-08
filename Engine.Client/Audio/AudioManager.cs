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
using Engine.Shared.IoC;
using Engine.Shared.Assets;
using Engine.Shared.Audio;
using Engine.Shared.Storage;

namespace Engine.Client.Audio;

public interface IAudioManager
{
    internal void Init();
    internal void Update(float dt);

    public StreamPackage? Play(string file, float volume = 1f, bool loop = false, float pitch = 0f);

    public bool TryPlay(string file, [NotNullWhen(true)] out StreamPackage? audio, float volume = 1f, bool loop = false, float pitch = 0f);

    public bool HasAudio(string audio);

    /// <summary>Stops playback and disposes the stream immediately.</summary>
    public void Stop(StreamPackage package);

    /// <summary>
    /// Live volume update for an already-playing stream.
    /// </summary>
    public void SetVolume(StreamPackage package, float volume);

    /// <summary>
    /// Live stereo pan update for an already-playing stream. -1 = full left, 1 = full right.
    /// </summary>
    public void SetPan(StreamPackage package, float pan);
}

internal sealed partial class AudioManager : IAudioManager
{
    /// <summary>
    /// The shared file/metadata manifest (see SharedAudioManager) - already populated by
    /// SharedContentManager.PostInit() by the time this manager's Init() runs.
    /// </summary>
    [Dependency] private readonly SharedAudioManager _registry = default!;

    public readonly List<(Stream stream, StreamPackage package)> RunningStreams = new();

    public AudioManager()
        => IoCManager.ResolveDependencies(this);

    void IAudioManager.Init()
    {
        MonoSoundLibrary.Init(GameClient.Instance);

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

    public StreamPackage? Play(string file, float volume = 1f, bool loop = false, float pitch = 0f)
    {
        TryPlay(file, out var audio, volume, loop, pitch);
        return audio;
    }

    public bool TryPlay(string file, [NotNullWhen(true)] out StreamPackage? audio, float volume = 1f, bool loop = false, float pitch = 0f)
    {
        audio = default;
        if (!HasAudio(file))
            return false;

        audio = GetPackage(file);
        if (audio is null)
            return false;

        audio.PlayingSound.Volume = volume;
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

            var (stream, pkg) = RunningStreams[i];
            RunningStreams.RemoveAt(i);
            pkg.Dispose();
            stream.Dispose();
            break;
        }
    }

    public bool HasAudio(string file)
        => _registry.HasAudio(file);

    public void SetVolume(StreamPackage package, float volume)
        => package.PlayingSound.Volume = volume;

    public void SetPan(StreamPackage package, float pan)
        => package.PlayingSound.Pan = pan;

    private StreamPackage? GetPackage(string relative)
    {
        if (!_registry.TryGetPath(relative, out var fullPath))
            return null;

        var stream = FileSystem.OpenRead(fullPath);
        stream.Position = 0;
        var sound = StreamLoader.GetStreamedSound(stream, AudioType.OGG, false);
        RunningStreams.Add((stream, sound));
        return sound;
    }
}