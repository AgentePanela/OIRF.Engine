using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Shared.GameObjects;
using Engine.Shared.GameObjects.Factories;
using Engine.Shared.IoC;
using Engine.Shared.Networking;

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

// requests a snapshot over the wire and caches whatever comes back
public sealed class RemoteViewVariablesAccess : IViewVariablesAccess
{
    private readonly INetManager _netMan;
    private readonly Dictionary<VVPath, VVSnapshot> _cache = new();
    private readonly Dictionary<uint, VVPath> _pendingRequests = new();
    private uint _nextRequestId = 1;

    public bool IsRemote => true;

    public RemoteViewVariablesAccess(INetManager netMan)
    {
        _netMan = netMan;
    }

    public VVSnapshot Snapshot(VVPath path)
    {
        if (_cache.TryGetValue(path, out var cached))
            return cached;

        if (!_pendingRequests.ContainsValue(path) && _netMan.MySession is { } session)
        {
            var id = _nextRequestId++;
            _pendingRequests[id] = path;
            session.SendMessage(new MsgViewVariablesRequest(id, path));
        }

        return new VVSnapshot { Path = path, Title = path.ToString(), Error = "Waiting on the server (not implemented yet)." };
    }

    public bool TryRead(VVPath path, out VVValue value)
    {
        // there's no per-value request message yet - only whole-snapshot metadata round-trips
        value = VVValue.Error("VV remote reads aren't wired up yet.");
        return false;
    }

    public bool TryWrite(VVPath path, object? newValue, string text, out string? error)
    {
        error = "VV remote is not implemented yet.";
        return false;
    }

    internal void OnResponseReceived(MsgViewVariablesResponse msg)
    {
        if (!_pendingRequests.TryGetValue(msg.RequestId, out var path))
            return;

        _pendingRequests.Remove(msg.RequestId);

        var members = new List<VVMemberInfo>(msg.Names.Count);
        for (var i = 0; i < msg.Names.Count; i++)
        {
            var enumNames = msg.EnumNames[i].Length == 0 ? null : msg.EnumNames[i].Split('|');
            members.Add(new VVMemberInfo(msg.Names[i], msg.TypeNames[i], null, msg.CanWrite[i],
                msg.Kinds[i], false, enumNames, path.Member(msg.Names[i])));
        }

        _cache[path] = new VVSnapshot
        {
            Path = path,
            Title = msg.Title,
            Error = msg.Error.Length == 0 ? null : msg.Error,
            Groups = [new VVGroup("", members)],
        };
    }
}

[RegisterIoC]
public sealed class ViewVariablesManager
{
    [Dependency] private readonly EntityManager _entMan = default!;
    [Dependency] private readonly ComponentFactory _compFac = default!;
    [Dependency] private readonly INetManager _netMan = default!;
    [Dependency] private readonly SharedContentManager _content = default!;

    private readonly Dictionary<int, object> _pins = new();
    private int _nextPinHandle = 1;

    private readonly IViewVariablesAccess _local;
    private readonly RemoteViewVariablesAccess _remote;

    public ViewVariablesManager()
    {
        IoCManager.ResolveDependencies(this);
        _local = new LocalViewVariablesAccess(new ViewVariablesResolver(_entMan, _compFac, TryGetPinned));
        _remote = new RemoteViewVariablesAccess(_netMan);
    }

    public void Init()
    {
        // registration is symmetric (no client/server split)
        _netMan.RegisterNetMessage<MsgViewVariablesRequest>((msg, session) =>
        {
            if (!_content.IsServer())
                return;

            Log.Warn("VV: remote requests are not implemented yet.");
        });

        _netMan.RegisterNetMessage<MsgViewVariablesResponse>((msg, _) =>
        {
            if (_content.IsServer())
                return;

            _remote.OnResponseReceived(msg);
        });

        _netMan.RegisterNetMessage<MsgViewVariablesWrite>((msg, session) =>
        {
            if (!_content.IsServer())
                return;

            Log.Warn("VV: remote writes are not implemented yet.");
        });
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
