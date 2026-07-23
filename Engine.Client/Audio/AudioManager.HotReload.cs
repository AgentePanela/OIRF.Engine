using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Engine.Shared.Assets;

namespace Engine.Client.Audio;

internal sealed partial class AudioManager
{
    private readonly ConcurrentQueue<(string Dir, string FullPath, string? OldFullPath)> _changedFiles = new();
    private readonly List<FileSystemWatcher> _watchers = new();

    private void InitHotReload()
    {
        foreach (var dir in resPath.GetFolders())
        {
            if (!Directory.Exists(dir))
                continue;

            var watcher = new FileSystemWatcher(dir)
            {
                IncludeSubdirectories = true,
                Filter = "*.ogg",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            watcher.Created += (_, e) => _changedFiles.Enqueue((dir, e.FullPath, null));
            watcher.Deleted += (_, e) => _changedFiles.Enqueue((dir, e.FullPath, null));
            watcher.Renamed += (_, e) => _changedFiles.Enqueue((dir, e.FullPath, e.OldFullPath));

            _watchers.Add(watcher);
        }
    }

    private void DrainHotReloadQueue()
    {
        while (_changedFiles.TryDequeue(out var change))
        {
            var (dir, fullPath, oldFullPath) = change;

            if (oldFullPath is not null)
            {
                var oldKey = SharedResourceManager.NormalizeKey(dir, oldFullPath);
                AudiosPath.Remove(oldKey);
            }

            var key = SharedResourceManager.NormalizeKey(dir, fullPath);

            if (File.Exists(fullPath))
            {
                if (AudiosPath.TryGetValue(key, out var existing) && existing != fullPath)
                    Log.Warn($"Audio '{key}' now maps to a different file ({existing} > {fullPath}).");

                AudiosPath[key] = fullPath;
                Log.Debug($"Hot-reloaded audio '{key}'.");
            }
            else if (AudiosPath.Remove(key))
            {
                Log.Debug($"Audio '{key}' deleted.");
            }
        }
    }
}
