using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Shared.Configuration;
using Engine.Shared.Configuration.CVars;
using Engine.Shared.GameObjects;
using Engine.Shared.GameObjects.Factories;
using Engine.Shared.IoC;
using Engine.Shared.Networking;
using Engine.Shared.Timing;

namespace Engine.Shared.Debug.ViewVariables;

/// <summary>
/// Reads and writes VV data for a path, wherever the target happens to live.
/// </summary>
public interface IViewVariablesAccess
{
    bool IsRemote { get; }

    VVSnapshot Snapshot(VVPath path);

    bool TryRead(VVPath path, out VVValue value);

    bool TryWrite(VVPath path, object? newValue, string text, out string? error);

    bool TryInsert(VVPath collectionPath, string? rawKey, out string? error);

    bool TryRemoveAt(VVPath elementPath, out string? error);

    bool TryAddComponent(VVRoot entityRoot, string sanitizedName, out string? error);

    bool TryRemoveComponent(VVRoot componentRoot, out string? error);

    List<string> GetAddableComponents(VVRoot entityRoot);

    /// <summary>
    /// Makes the next <see cref="Snapshot"/> for a path go back to the source instead of answering from a cache
    /// </summary>
    void Invalidate(VVPath path);
}

public sealed class LocalViewVariablesAccess : IViewVariablesAccess
{
    private readonly ViewVariablesResolver _resolver;
    private readonly EntityManager _entMan;
    private readonly ComponentFactory _compFac;

    public bool IsRemote => false;

    public LocalViewVariablesAccess(ViewVariablesResolver resolver, EntityManager entMan, ComponentFactory compFac)
    {
        _resolver = resolver;
        _entMan = entMan;
        _compFac = compFac;
    }

    public VVSnapshot Snapshot(VVPath path) => _resolver.Snapshot(path);

    public bool TryRead(VVPath path, out VVValue value) => _resolver.TryRead(path, out value);

    public bool TryWrite(VVPath path, object? newValue, string text, out string? error)
        => _resolver.TryWrite(path, newValue, out error);

    public bool TryWriteText(VVPath path, string text, out string? error, Func<int, EntityUid>? entityFromNet = null)
        => _resolver.TryWriteText(path, text, out error, entityFromNet);

    public bool TryInsert(VVPath collectionPath, string? rawKey, out string? error)
        => _resolver.TryInsert(collectionPath, rawKey, out error);

    public bool TryRemoveAt(VVPath elementPath, out string? error)
        => _resolver.TryRemoveAt(elementPath, out error);

    public bool TryAddComponent(VVRoot entityRoot, string sanitizedName, out string? error)
    {
        if (!_compFac.ComponentsSanitized.TryGetValue(sanitizedName, out var type))
        {
            error = Loc.GetString("engine-vv-error-unknown-component", ("name", sanitizedName));
            return false;
        }

        var uid = new EntityUid(entityRoot.Uid);
        if (_entMan.TryComp(uid, type, out _))
        {
            error = Loc.GetString("engine-vv-error-already-has-component", ("name", sanitizedName));
            return false;
        }

        _entMan.AddComponent(uid, type);
        error = null;
        return true;
    }

    public bool TryRemoveComponent(VVRoot componentRoot, out string? error)
    {
        if (componentRoot.Kind != VVRootKind.Component || componentRoot.ComponentTypeName is null)
        {
            error = Loc.GetString("engine-vv-error-not-a-component");
            return false;
        }

        var type = _compFac.GetTypeByString(componentRoot.ComponentTypeName);
        if (type is null)
        {
            error = Loc.GetString("engine-vv-error-unknown-component-type", ("name", componentRoot.ComponentTypeName));
            return false;
        }

        _entMan.RemComp(new EntityUid(componentRoot.Uid), type);
        error = null;
        return true;
    }

    public List<string> GetAddableComponents(VVRoot entityRoot)
    {
        var existing = _entMan.GetEntityComps(new EntityUid(entityRoot.Uid))?.Select(c => c.GetType()).ToHashSet() ?? [];

        return _compFac.ComponentsSanitized
            .Where(kv => !existing.Contains(kv.Value))
            .Select(kv => kv.Key)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Invalidate(VVPath path)
    {
    }
}

/// <summary>
/// The other process' side of VV: asks the server for whole snapshots (values included) and caches them, and sends
/// every mutation as one write message. <para/>
/// The interface is synchronous but the wire is not, so a mutation the server refuses is reported later through
/// <see cref="OnRemoteError"/> instead of coming back out of the call.
/// </summary>
public sealed class RemoteViewVariablesAccess : IViewVariablesAccess
{
    private readonly INetManager _netMan;
    private readonly IGameTiming _timing;
    private readonly IConfigurationManager _cfg;
    private readonly ComponentFactory _compFac;

    private readonly Dictionary<VVPath, VVSnapshot> _cache = new();
    private readonly Dictionary<VVPath, VVValue> _memberValues = new();
    private readonly Dictionary<VVPath, double> _requestedAt = new();
    private readonly Dictionary<uint, VVPath> _pendingSnapshots = new();
    private readonly HashSet<VVPath> _forced = new();
    private readonly HashSet<uint> _pendingWrites = new();
    private uint _nextRequestId = 1;

    public bool IsRemote => true;

    /// <summary>
    /// A mutation the server refused. Nothing ties it back to the call that caused it, so it is just text.
    /// </summary>
    public event Action<string>? OnRemoteError;

    public RemoteViewVariablesAccess(INetManager netMan, IGameTiming timing, IConfigurationManager cfg,
        ComponentFactory compFac)
    {
        _netMan = netMan;
        _timing = timing;
        _cfg = cfg;
        _compFac = compFac;
    }

    public VVSnapshot Snapshot(VVPath path)
    {
        var hasCache = _cache.TryGetValue(path, out var cached);
        var interval = _cfg.Get(EngineCvars.VVRemoteRefresh);
        var age = _timing.TotalTime - _requestedAt.GetValueOrDefault(path, double.NegativeInfinity);

        if ((!hasCache || (interval > 0f && age >= interval) || _forced.Contains(path))
            && !_pendingSnapshots.ContainsValue(path))
        {
            Request(path);
        }

        return cached ?? new VVSnapshot { Path = path, Title = path.ToString(), Error = Loc.GetString("engine-vv-waiting-server") };
    }

    public bool TryRead(VVPath path, out VVValue value)
    {
        if (_memberValues.TryGetValue(path, out var cached))
        {
            value = cached;
            return true;
        }

        value = VVValue.Error(Loc.GetString("engine-vv-waiting-server"));
        return false;
    }

    public bool TryWrite(VVPath path, object? newValue, string text, out string? error)
        => Send(path, VVWriteOp.Write, text, out error);

    public bool TryInsert(VVPath collectionPath, string? rawKey, out string? error)
        => Send(collectionPath, VVWriteOp.Insert, rawKey ?? "", out error);

    public bool TryRemoveAt(VVPath elementPath, out string? error)
        => Send(elementPath, VVWriteOp.RemoveAt, "", out error);

    public bool TryAddComponent(VVRoot entityRoot, string sanitizedName, out string? error)
        => Send(VVPath.Of(entityRoot), VVWriteOp.AddComponent, sanitizedName, out error);

    public bool TryRemoveComponent(VVRoot componentRoot, out string? error)
        => Send(VVPath.Of(componentRoot), VVWriteOp.RemoveComponent, "", out error);

    public List<string> GetAddableComponents(VVRoot entityRoot)
    {
        // only what this build knows about can be offered - a server-only component is not in the client's factory
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_cache.TryGetValue(VVPath.Of(entityRoot), out var snapshot))
        {
            foreach (var group in snapshot.Groups)
            {
                if (group.Path is not null)
                    existing.Add(group.Name);
            }
        }

        return _compFac.ComponentsSanitized.Keys
            .Where(name => !existing.Contains(name))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Invalidate(VVPath path) => _forced.Add(path);

    private void Request(VVPath path)
    {
        if (_netMan.MySession is not { } session)
            return;

        var id = _nextRequestId++;
        _pendingSnapshots[id] = path;
        _requestedAt[path] = _timing.TotalTime;
        _forced.Remove(path);
        session.SendMessage(new MsgViewVariablesRequest(id, path));
    }

    private bool Send(VVPath path, VVWriteOp op, string value, out string? error)
    {
        if (_netMan.MySession is not { } session)
        {
            error = Loc.GetString("engine-vv-error-not-connected");
            return false;
        }

        var id = _nextRequestId++;
        _pendingWrites.Add(id);
        session.SendMessage(new MsgViewVariablesWrite(id, path, op, value));

        // whatever it did over there, what is cached here is now a lie
        Invalidate(path);
        if (path.Parent is { } parent)
            Invalidate(parent);

        error = null;
        return true;
    }

    internal void OnResponseReceived(MsgViewVariablesResponse msg)
    {
        // a write only ever gets an error back, so the request id is what tells the two replies apart
        if (_pendingWrites.Remove(msg.RequestId))
        {
            if (msg.Error.Length > 0)
                OnRemoteError?.Invoke(msg.Error);

            return;
        }

        if (!_pendingSnapshots.Remove(msg.RequestId, out var path))
            return;

        var groups = new List<VVGroup>(msg.GroupNames.Count);
        var netEnt = new NetEntity(path.Root.Uid);
        var next = 0;

        // the window rebuilds its rows when this changes and only refreshes values otherwise - a response lands every
        // refresh interval, and rebuilding that often would throw away whatever the user is typing
        var structure = new HashCode();

        for (var g = 0; g < msg.GroupNames.Count; g++)
        {
            var count = msg.GroupMemberCounts[g];
            var members = new List<VVMemberInfo>(count);
            structure.Add(msg.GroupNames[g]);
            structure.Add(msg.GroupComponents[g]);

            for (var i = next; i < next + count; i++)
            {
                var memberPath = path.Member(msg.Names[i]);
                var enumNames = msg.EnumNames[i].Length == 0 ? null : msg.EnumNames[i].Split('|');
                var type = ViewVariablesRemoteTypes.Resolve(msg.TypeNames[i]);

                members.Add(new VVMemberInfo(msg.Names[i], msg.TypeNames[i], type, msg.CanWrite[i], msg.Kinds[i],
                    msg.Drillable[i], enumNames, memberPath, msg.CanRemove[i]));

                _memberValues[memberPath] = BuildValue(msg, i, type);
                structure.Add(msg.Names[i]);
            }

            next += count;

            // a group that names a component is a component entry - it carries its own root so the window can open it
            var groupPath = msg.GroupComponents[g].Length == 0
                ? null
                : VVPath.Of(VVRoot.ServerComponent(netEnt, msg.GroupComponents[g]));

            groups.Add(new VVGroup(msg.GroupNames[g], members, groupPath));
        }

        _cache[path] = new VVSnapshot
        {
            Path = path,
            Title = msg.Title,
            Error = msg.Error.Length == 0 ? null : msg.Error,
            Groups = groups,
            Collection = msg.IsCollection ? new VVCollectionInfo(msg.IsDictionary, msg.CanInsert) : null,
            StructureVersion = structure.ToHashCode(),
        };
    }

    // parsing the text back into a real object is what lets the existing editors work on a remote member at all -
    // every one of them dispatches on the type and reads VVValue.Local
    private static VVValue BuildValue(MsgViewVariablesResponse msg, int index, Type? type)
    {
        object? local = null;
        if (type is not null)
            ViewVariablesConvert.TryParse(type, msg.Texts[index], out local, out _);

        return new VVValue
        {
            Kind = msg.Kinds[index],
            Text = msg.Texts[index],
            TypeName = msg.TypeNames[index],
            Count = msg.Counts[index],
            Local = local,
            LocalType = type,
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

    private LocalViewVariablesAccess _local = default!;

    /// <summary>
    /// The server-side view. Public so the window can listen to <see cref="RemoteViewVariablesAccess.OnRemoteError"/>.
    /// </summary>
    public RemoteViewVariablesAccess Remote { get; private set; } = default!;

    public ViewVariablesManager()
    {
        IoCManager.ResolveDependencies(this);
    }

    public void Init()
    {
        // built here instead of in the ctor: timing and config aren't registered yet while IoC is auto-registering
        _local = new LocalViewVariablesAccess(new ViewVariablesResolver(_entMan, _compFac, TryGetPinned), _entMan, _compFac);
        Remote = new RemoteViewVariablesAccess(_netMan, IoCManager.Resolve<IGameTiming>(),
            IoCManager.Resolve<IConfigurationManager>(), _compFac);

        // registration is symmetric (no client/server split)
        _netMan.RegisterNetMessage<MsgViewVariablesRequest>((msg, session) =>
        {
            if (!_content.IsServer() || session is null)
                return;

            OnRemoteRequest(msg, session);
        });

        _netMan.RegisterNetMessage<MsgViewVariablesResponse>((msg, _) =>
        {
            if (_content.IsServer())
                return;

            Remote.OnResponseReceived(msg);
        });

        _netMan.RegisterNetMessage<MsgViewVariablesWrite>((msg, session) =>
        {
            if (!_content.IsServer() || session is null)
                return;

            OnRemoteWrite(msg, session);
        });
    }

    public IViewVariablesAccess For(VVRoot root) => root.Side == VVSide.Server ? Remote : _local;

    private void OnRemoteRequest(MsgViewVariablesRequest msg, INetSession session)
    {
        if (!TryRebase(msg.ToPath(), out var localPath, out var error))
        {
            session.SendMessage(new MsgViewVariablesResponse(msg.RequestId, error!));
            return;
        }

        session.SendMessage(new MsgViewVariablesResponse(msg.RequestId, _local.Snapshot(localPath!), ReadForRemote));
    }

    private void OnRemoteWrite(MsgViewVariablesWrite msg, INetSession session)
    {
        if (!TryRebase(msg.ToPath(), out var localPath, out var error))
        {
            session.SendMessage(new MsgViewVariablesResponse(msg.RequestId, error!));
            return;
        }

        switch ((VVWriteOp)msg.Op)
        {
            case VVWriteOp.Write:
                // only the text crossed the wire, so it gets parsed here against the member's own type
                _local.TryWriteText(localPath!, msg.Value, out error, id => _entMan.GetEntity(new NetEntity(id)));
                break;
            case VVWriteOp.Insert:
                _local.TryInsert(localPath!, msg.Value.Length == 0 ? null : msg.Value, out error);
                break;
            case VVWriteOp.RemoveAt:
                _local.TryRemoveAt(localPath!, out error);
                break;
            case VVWriteOp.AddComponent:
                _local.TryAddComponent(localPath!.Root, msg.Value, out error);
                break;
            case VVWriteOp.RemoveComponent:
                _local.TryRemoveComponent(localPath!.Root, out error);
                break;
            default:
                error = Loc.GetString("engine-vv-error-unknown-write-op", ("op", msg.Op));
                break;
        }

        session.SendMessage(new MsgViewVariablesResponse(msg.RequestId, error ?? ""));
    }

    /// <summary>
    /// Turns a path addressed at this process from the outside into one the local resolver understands: the root comes
    /// in as a <see cref="NetEntity"/>, which means nothing to the resolver.
    /// </summary>
    private bool TryRebase(VVPath? path, out VVPath? local, out string? error)
    {
        local = null;

        if (path is null)
        {
            error = Loc.GetString("engine-vv-error-malformed-path");
            return false;
        }

        if (path.Root.Side != VVSide.Server)
        {
            error = Loc.GetString("engine-vv-error-not-server-path");
            return false;
        }

        if (path.Root.Kind == VVRootKind.Detached)
        {
            error = Loc.GetString("engine-vv-error-detached-local-only");
            return false;
        }

        var netEnt = new NetEntity(path.Root.Uid);
        if (!_entMan.TryGetEntity(netEnt, out var uid))
        {
            error = Loc.GetString("engine-vv-error-net-entity-missing", ("netEntity", netEnt));
            return false;
        }

        var root = path.Root.Kind == VVRootKind.Component
            ? new VVRoot(VVRootKind.Component, uid.Id, path.Root.ComponentTypeName, 0)
            : VVRoot.Entity(uid);

        local = path.WithRoot(root);
        error = null;
        return true;
    }

    private VVValue ReadForRemote(VVMemberInfo member)
    {
        if (!_local.TryRead(member.Path, out var value))
            return value;

        // a local uid means nothing on the other side - hand over the NetEntity instead
        if (value.Local is EntityUid uid)
        {
            return new VVValue
            {
                Kind = value.Kind,
                TypeName = value.TypeName,
                Count = value.Count,
                Text = _entMan.GetNetEntity(uid).Id.ToString(),
            };
        }

        return value;
    }

    public int Pin(object obj)
    {
        var handle = _nextPinHandle++;
        _pins[handle] = obj;
        return handle;
    }

    public void Unpin(int handle) => _pins.Remove(handle);

    private object? TryGetPinned(int handle) => _pins.GetValueOrDefault(handle);

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
