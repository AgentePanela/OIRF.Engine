using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Engine.Shared.Locale;

internal sealed partial class LocalizationManager
{
    private readonly ConcurrentQueue<string> _changedFiles = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private bool _hotReloadInited;

    private void InitHotReload()
    {
        #if !DEBUG
        return;
        #endif

        if (_hotReloadInited)
            return;
        _hotReloadInited = true;

        foreach (var dir in ResPath.GetFolders())
        {
            if (!Directory.Exists(dir))
                continue;

            var watcher = new FileSystemWatcher(dir)
            {
                IncludeSubdirectories = true,
                Filter = "*.ftl",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            watcher.Changed += OnFileChanged;
            watcher.Renamed += OnFileChanged;

            _watchers.Add(watcher);
        }
    }

    public void Update()
    {
        #if !DEBUG
        return;
        #endif

        var changed = false;
        while (_changedFiles.TryDequeue(out _))
            changed = true;

        if (!changed)
            return;

        try
        {
            ReloadCulture();
            Log.Debug("FTL files updaed. Reloading...");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to hot-reload locale: {ex.Message}");
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        _changedFiles.Enqueue(e.FullPath);
    }
}
