using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Shared.GameObjects;
using Engine.Shared.GameObjects.Factories;
using Engine.Shared.IoC;

namespace Engine.Shared.Debug.ViewVariables;

/// <summary>
/// Reads (and, eventually, writes) VV data for a path.
/// </summary>
public interface IViewVariablesAccess
{
    bool IsRemote { get; }

    VVSnapshot Snapshot(VVPath path);

    bool TryRead(VVPath path, out VVValue value);

    bool TryWrite(VVPath path, object? newValue, string text, out string? error);
}

public sealed class LocalViewVariablesAccess : IViewVariablesAccess
{
    private readonly ViewVariablesResolver _resolver;

    public bool IsRemote => false;

    public LocalViewVariablesAccess(ViewVariablesResolver resolver)
    {
        _resolver = resolver;
    }

    public VVSnapshot Snapshot(VVPath path) => _resolver.Snapshot(path);

    public bool TryRead(VVPath path, out VVValue value) => _resolver.TryRead(path, out value);

    public bool TryWrite(VVPath path, object? newValue, string text, out string? error)
        => _resolver.TryWrite(path, newValue, out error);
}

public sealed class RemoteViewVariablesAccess : IViewVariablesAccess
{
    public bool IsRemote => true;

    public VVSnapshot Snapshot(VVPath path) => new()
    {
        Path = path,
        Title = path.ToString(),
        Error = "VV remote is not implemented yet.",
    };

    public bool TryRead(VVPath path, out VVValue value)
    {
        value = VVValue.Error("VV remote is not implemented yet.");
        return false;
    }

    public bool TryWrite(VVPath path, object? newValue, string text, out string? error)
    {
        error = "VV remote is not implemented yet.";
        return false;
    }
}

[RegisterIoC]
public sealed class ViewVariablesManager
{
    [Dependency] private readonly EntityManager _entMan = default!;
    [Dependency] private readonly ComponentFactory _compFac = default!;

    private readonly Dictionary<int, object> _pins = new();
    private int _nextPinHandle = 1;

    private readonly IViewVariablesAccess _local;

    public ViewVariablesManager()
    {
        IoCManager.ResolveDependencies(this);
        _local = new LocalViewVariablesAccess(new ViewVariablesResolver(_entMan, _compFac, TryGetPinned));
    }

    public IViewVariablesAccess For(VVRoot root) => _local;

    public int Pin(object obj)
    {
        var handle = _nextPinHandle++;
        _pins[handle] = obj;
        return handle;
    }

    public void Unpin(int handle) => _pins.Remove(handle);

    private object? TryGetPinned(int handle) => _pins.GetValueOrDefault(handle);

    public bool TryRemoveComponent(VVRoot root, out string? error)
    {
        if (root.Kind != VVRootKind.Component || root.ComponentTypeName is null)
        {
            error = "Not a component.";
            return false;
        }

        var type = _compFac.GetTypeByString(root.ComponentTypeName);
        if (type is null)
        {
            error = $"Unknown component type '{root.ComponentTypeName}'.";
            return false;
        }

        _entMan.RemComp(new EntityUid(root.Uid), type);
        error = null;
        return true;
    }

    public bool TryAddComponent(EntityUid uid, string sanitizedName, out string? error)
    {
        if (!_compFac.ComponentsSanitized.TryGetValue(sanitizedName, out var type))
        {
            error = $"Unknown component '{sanitizedName}'.";
            return false;
        }

        if (_entMan.TryComp(uid, type, out _))
        {
            error = $"Entity already has '{sanitizedName}'.";
            return false;
        }

        _entMan.AddComponent(uid, type);
        error = null;
        return true;
    }

    public List<string> GetAddableComponents(EntityUid uid)
    {
        var existing = _entMan.GetEntityComps(uid)?.Select(c => c.GetType()).ToHashSet() ?? [];

        return _compFac.ComponentsSanitized
            .Where(kv => !existing.Contains(kv.Value))
            .Select(kv => kv.Key)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // tells the client to open the vv window
    public event Action<VVPath>? OnOpenRequested;

    public bool TryRequestOpen(VVPath path)
    {
        if (OnOpenRequested is null)
            return false;

        OnOpenRequested(path);
        return true;
    }
}
