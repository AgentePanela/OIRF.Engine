using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Engine.Shared.Assets;
using Engine.Shared.Storage;

namespace Engine.Shared.Audio;

/// <summary>
/// Shared audio manifest: scans the "Audio" resource root for .ogg files and reads their
/// metadata (duration/sample rate/channels).
/// </summary>
public sealed class SharedAudioManifest
{
    public readonly ResPath ResPath = new("Audios");

    private readonly Dictionary<string, string> _paths = new();
    private readonly Dictionary<string, AudioMetadata> _metadata = new();

    public void Load()
    {
        foreach (var dir in ResPath.GetFolders())
        {
            foreach (var file in FileSystem.GetFiles(dir, "*.ogg"))
            {
                var key = SharedResourceManager.NormalizeKey(dir, file);
                if (HasAudio(key))
                    throw new Exception($"{key} is already loaded. Make sure you dont have a duplicated sound in audio and music folders.");

                Upsert(key, file);
            }
        }
    }

    /// <summary>Adds or replaces a key's path (and re-reads its metadata) - also used by the client's hot reload.</summary>
    public void Upsert(string key, string fullPath)
    {
        _paths[key] = fullPath;

        using var stream = FileSystem.OpenRead(fullPath);
        if (AudioMetadataReader.TryRead(stream, out var metadata))
            _metadata[key] = metadata;
        else
            Log.Warn($"Failed to read audio metadata for '{key}' ({fullPath}).");
    }

    public bool Remove(string key)
    {
        _metadata.Remove(key);
        return _paths.Remove(key);
    }

    public bool HasAudio(string key)
        => _paths.ContainsKey(key);

    public bool TryGetPath(string key, [NotNullWhen(true)] out string? path)
        => _paths.TryGetValue(key, out path);

    public bool TryGetMetadata(string key, out AudioMetadata metadata)
        => _metadata.TryGetValue(key, out metadata);

    public List<string> GetAudioKeys()
        => new(_paths.Keys);
}
