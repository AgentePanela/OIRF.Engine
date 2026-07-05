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

namespace Engine.Client.Audio;

public interface IAudioManager
{
    internal void Init();
    internal void Update(float dt);

    public StreamPackage? Play(string file, float volume = 1f, bool loop = false);

    public bool TryPlay(string file, [NotNullWhen(true)] out StreamPackage? audio, float volume = 1f, bool loop = false);

    public bool HasAudio(string audio);
}

internal sealed class AudioManager : IAudioManager
{
    /// <summary>
    /// Stores the relative path | full path for a audio file, all audio files that will be played 
    /// must be present in this dictionary that is filled during the game loading.
    /// </summary>
    public readonly Dictionary<string, string> AudiosPath = new();
    /// <summary>
    /// <relative path, stream>
    /// </summary>
    public readonly Dictionary<string, FileStream> CachedStreams = new();
    /// <summary>
    /// Null filestream means that this is using a cached stream.
    /// </summary>
    public readonly List<(FileStream? stream, StreamPackage package)> RunningStreams = new();

    public readonly ResPath resPath = new("Audio");

    public AudioManager()
        => IoCManager.ResolveDependencies(this);

    void IAudioManager.Init()
    {
        MonoSoundLibrary.Init(GameClient.Instance);
        var audioDirs = resPath.GetFolders();
        LoadFiles(audioDirs);

        //var musicDir = Path.Combine(_asset.GetResourcesFolder(), "Music");
        //LoadFiles(musicDir, false /* todo: change how cache works (use byte[] instead)*/); // will keep the audios in stream 

        GameClient.Instance.Exiting += (_, _) => MonoSoundLibrary.DeInit();
    }

    void IAudioManager.Update(float dt)
    {
        for (int i = RunningStreams.Count - 1; i >= 0; i--)
        {
            var sound = RunningStreams[i];

            if (sound.package.FinishedStreaming)
            {
                sound.package.Dispose();
                sound.stream?.Dispose();
                RunningStreams.RemoveAt(i);
            }
        }
    }

    public void LoadFiles(string[] dirs, bool cache = false)
    {
        foreach (var dir in dirs)
        {
            var files = Directory.GetFiles(dir, "*.ogg", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var relative = SharedResourceManager.NormalizeKey(dir, file);
                if (HasAudio(relative))
                    throw new Exception($"{relative} is already loaded. Make sure you dont have a duplicated sound in audio and music folders.");

                AudiosPath.Add(relative, file);
                
                if (cache)
                    CachedStreams.Add(relative, File.OpenRead(file));
            }
        }
    }

    public StreamPackage? Play(string file, float volume = 1f, bool loop = false)  
    {
        TryPlay(file, out var audio, volume, loop);
        return audio;
    }

    public bool TryPlay(string file, [NotNullWhen(true)] out StreamPackage? audio, float volume = 1f, bool loop = false)
    {
        audio = default;
        if (!HasAudio(file))
            return false;

        audio = GetPackage(file);
        if (audio is null)
            return false;

        audio.PlayingSound.Volume = volume;
        audio.IsLooping = loop;
        audio.Play();
        return true;
    }

    public bool HasAudio(string file)
        => AudiosPath.ContainsKey(file);

    private StreamPackage? GetPackage(string relative)
    {
        var cached = true;
        if (!CachedStreams.TryGetValue(relative, out var stream))
        {
            if (!AudiosPath.TryGetValue(relative, out var fullPath))
                return null;
            
            stream = File.OpenRead(fullPath);
            cached = false;
        }
        
        stream.Position = 0;
        var sound = StreamLoader.GetStreamedSound(stream, AudioType.OGG, false);
        if (cached)
            RunningStreams.Add((null, sound));
        else
            RunningStreams.Add((stream, sound));
        
        return sound;
    }
}