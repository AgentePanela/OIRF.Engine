using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Engine.Shared.Prototypes;

public sealed partial class PrototypeManager
{
    private readonly ConcurrentQueue<string> _changedFiles = new();
    private readonly List<FileSystemWatcher> _watchers = new();

    private void InitHotReload()
    {
        #if !DEBUG
        return;
        #endif

        foreach (var dir in ResPath.GetFolders())
        {
            if (!Directory.Exists(dir))
                continue;

            var watcher = new FileSystemWatcher(dir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            watcher.Filters.Add("*.yml");
            watcher.Filters.Add("*.yaml");
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

        var reloaded = new HashSet<string>();
        while (_changedFiles.TryDequeue(out var file))
        {
            if (reloaded.Add(file))
                ReloadFile(file);
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        _changedFiles.Enqueue(e.FullPath);
    }

    private void ReloadFile(string filePath)
    {
        if (!File.Exists(filePath))
            return;

        var oldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (typeKey, bucket) in _rawByType)
        {
            foreach (var id in bucket.Where(kv => kv.Value.SourceFile == filePath).Select(kv => kv.Key).ToList())
            {
                bucket.Remove(id);
                oldKeys.Add(typeKey);
            }
        }

        List<RawPrototype> raws;
        try
        {
            raws = new PrototypeLoader().LoadFile(filePath, IgnoredPrototypesTypes);
        }
        catch (PrototypeLoadException ex)
        {
            Log.Error($"Failed to hot-reload prototype file '{filePath}': {ex.Message}");
            return;
        }

        var newKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in raws)
        {
            if (string.IsNullOrWhiteSpace(raw.Type) || string.IsNullOrWhiteSpace(raw.ID))
            {
                Log.Error($"Prototype in '{filePath}' is missing 'type' or 'id'; skipping.");
                continue;
            }

            if (!_typeMapping.ContainsKey(raw.Type))
            {
                if (!IgnoredPrototypesTypes.Contains(raw.Type))
                    Log.Error($"Unknown prototype type '{raw.Type}' in '{filePath}'; skipping.");
                continue;
            }

            if (!_rawByType.TryGetValue(raw.Type, out var bucket))
            {
                bucket = new Dictionary<string, RawPrototype>(StringComparer.OrdinalIgnoreCase);
                _rawByType[raw.Type] = bucket;
            }

            if (bucket.ContainsKey(raw.ID))
            {
                Log.Error($"Duplicate prototype id '{raw.ID}' (type '{raw.Type}') in '{filePath}'; skipping.");
                continue;
            }

            bucket[raw.ID] = raw;
            newKeys.Add(raw.Type);
        }

        var affectedKeys = oldKeys
            .Concat(newKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key => (Key: key, Priority: _typeMapping[key].GetCustomAttribute<PrototypeAttribute>()!.LoadPriority))
            .OrderBy(x => x.Priority)
            .Select(x => x.Key);

        var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var typeKey in affectedKeys)
            RebuildTypeKey(typeKey, stack);

        Log.Debug($"Hot-reloaded prototypes from '{filePath}'.");
    }

    /// <summary>
    /// Rebuilds all prototypes of a single type key after an incremental reload, reusing existing
    /// instances in place (same reference) so other systems already holding a resolved prototype
    /// see the updated data. New ids get a new instance; ids no longer present are dropped from the index.
    /// </summary>
    private void RebuildTypeKey(string typeKey, HashSet<string> stack)
    {
        var clrType = _typeMapping[typeKey];

        foreach (var cacheKey in _mergedCache.Keys.Where(k => k.TypeKey.Equals(typeKey, StringComparison.OrdinalIgnoreCase)).ToList())
            _mergedCache.Remove(cacheKey);

        _rawByType.TryGetValue(typeKey, out var bucket);
        var newIds = bucket?.Keys ?? Enumerable.Empty<string>();

        _index.TryGetValue(clrType, out var existingDict);
        if (existingDict is not null)
        {
            foreach (var oldId in existingDict.Keys.ToList())
            {
                if (bucket is null || !bucket.ContainsKey(oldId))
                    existingDict.Remove(oldId);
            }
        }

        foreach (var id in newIds)
        {
            var raw = bucket![id];
            var mergedFields = GetMergedFields(typeKey, raw, stack);

            IPrototype? instance = null;
            existingDict?.TryGetValue(id, out instance);

            instance ??= (IPrototype)Activator.CreateInstance(clrType)!;
            PopulateInstance(instance, raw, mergedFields);

            if (instance is IInheritingPrototype inh && inh.Abstract)
                existingDict?.Remove(id);
            else
                AddToIndex(instance);
        }
    }
}
