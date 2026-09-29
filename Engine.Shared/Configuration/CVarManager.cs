using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Engine.Shared.IoC;
using Engine.Shared.Networking;
using Engine.Shared.Storage;

namespace Engine.Shared.Configuration;

/// <summary>
/// Manages communication with the CVars (console variables). CVars are used to maintain a configuration variable, like, if autosave is enabled.
/// A lot more can be done with CVars.
/// </summary>
public interface IConfigurationManager
{
    internal void Init();

    /// <summary>
    /// Get a cvar current value.
    /// </summary>
    public T Get<T>(CVarDef<T> cvar);

    /// <summary>
    /// Set a cvar value.
    /// </summary>
    public void Set<T>(CVarDef<T> cvar, T value);

    internal void ForceDefaultValue<T>(CVarDef<T> cvar, T value);

    /// <summary>
    /// Subscribe to a cvar, this event will happend everytime the cvar is changed.
    /// </summary>
    /// <param name="invokeImmediately">Will run when the subscribe is added?</param>
    public void Subs<T>(CVarDef<T> cvar, Action<T> callback, bool invokeImmediately = true);

    /// <summary>
    /// Removes a callback added with <see cref="Subs{T}"/>. It has to be the same delegate.
    /// </summary>
    public void Unsubs<T>(CVarDef<T> cvar, Action<T> callback);

    /// <summary>
    /// Removes every cvar callback from the instance.
    /// </summary>
    public void UnsubsAll(object owner);

    /// <summary>
    /// Save all cvars that have the current values different from the default ones to the dataPath/config.toml
    /// except the <see cref="CVar.NOSAVE"/> ones.
    /// </summary>
    public void SaveConfig();

    /// <summary>
    /// Load the config in dataPath/config.toml
    /// </summary>
    public void LoadConfig();

    /// <summary>
    /// Inject a cvar def into the loaded cvar list.
    /// </summary>
    public void InjectCVar(CVarDef? def);

    /// <summary>
    /// Gets a cvar's current value by name. False if no such cvar is registered.
    /// </summary>
    public bool TryGetByName(string name, out object? value);

    /// <summary>
    /// Parses <paramref name="rawValue"/> for whatever type the named cvar actually is and sets
    /// it.
    /// </summary>
    public bool TrySetByName(string name, string rawValue, out string? error);

    /// <summary>
    /// Every registered cvars name.
    /// </summary>
    public IEnumerable<string> AllCVarNames { get; }

    public event Action? OnConfigLoad;
}

public sealed class ConfigurationManager : IConfigurationManager
{
    [Dependency] private UserStorageManager _storage = default!;
    [Dependency] private SharedContentManager _sharedContent = default!;
    [Dependency] private INetManager _netMan = default!;
    private readonly Dictionary<string, object> _values = new();
    private readonly Dictionary<string, CVarDef> _defs = new(); // default values
    private readonly Dictionary<string, List<Delegate>> _subscribers = new();
    private readonly Dictionary<string, string> _unknownValues = new();

    /// <summary>
    /// Invoked when the config is loaded.
    /// </summary>
    public event Action? OnConfigLoad;

    void IConfigurationManager.Init()
    {
        IoCManager.ResolveDependencies(this);
        LoadCVars();
        LoadConfig();

        _netMan.RegisterNetMessage<MsgReplicateCvar>((msg, _) => ApplyReplicatedValue(msg.Name, msg.Value));
        _netMan.OnConnected += OnSessionConnected;
    }

    // sync current cvars state
    private void OnSessionConnected(object? sender, NetSessionArgs args)
    {
        if (!_sharedContent.IsServer() || args.Session is null)
            return;

        foreach (var (name, def) in _defs)
        {
            if (!def.Flags.HasFlag(CVar.REPLICATED))
                continue;

            args.Session.SendMessage(new MsgReplicateCvar(name, FormatToml(_values[name])));
        }
    }

    /// <summary>
    /// Applies a cvar value that came from the network (CLIENT-SIDE)
    /// </summary>
    private void ApplyReplicatedValue(string name, string rawValue)
    {
        if (_sharedContent.IsServer())
        {
            Log.Warn($"Received a MsgReplicateCvar for '{name}' on the server - ignoring.");
            return;
        }

        if (!_defs.TryGetValue(name, out var def))
            return;

        var value = ParseTomlValue(rawValue, def);
        _values[name] = value;

        if (_subscribers.TryGetValue(name, out var list))
            def.FireSubscribers(value, list);

        Log.Warn($"Updated REPLCIATED cvar {name} to new value ({rawValue})!");
    }

    void IConfigurationManager.ForceDefaultValue<T>(CVarDef<T> cvar, T value)
    {
        cvar.DefaultValue = value;
        if (cvar.Flags.HasFlag(CVar.REPLICATED) && _netMan.IsServer)
            BroadcastCvarChange(cvar, value);
    }

    internal void LoadCVars()
    {
        var defs = FindCVars();

        foreach (var def in defs)
            InjectCVar(def);
    }

    public void InjectCVar(CVarDef? def)
    {
        if (def is null)
            return;

        if (def.Flags.HasFlag(CVar.SERVERONLY) && !_sharedContent.IsServer())
            return;

        if (def.Flags.HasFlag(CVar.CLIENTONLY) && !_sharedContent.IsClient())
            return;

        var type = def.GetType();
        var prop = type.GetProperty("DefaultValue");

        var value = prop?.GetValue(def);

        _defs[def.Name] = def;
        _values[def.Name] = value!;
        Log.Debug($"New cvar! {def.Name}");

        if (!_unknownValues.Remove(def.Name, out var raw))
            return;

        try
        {
            value = ParseTomlValue(raw, def);
        }
        catch (Exception e)
        {
            Log.Warn($"Couldn't parse '{raw}' from the config file for cvar '{def.Name}', keeping the default: {e.Message}");
            return;
        }

        _values[def.Name] = value;

        if (_subscribers.TryGetValue(def.Name, out var list))
            def.FireSubscribers(value, list);
    }

    public T Get<T>(CVarDef<T> cvar)
    {
        return (T)_values[cvar.Name];
    }

    public void Set<T>(CVarDef<T> cvar, T value)
    {
        if (cvar.Flags.HasFlag(CVar.SERVER) && !_sharedContent.IsServer())
            throw new InvalidOperationException($"Cvar '{cvar.Name}' can only be set by the server.");

        _values[cvar.Name] = value!;

        if (_subscribers.TryGetValue(cvar.Name, out var list))
        {
            foreach (var sub in list)
            {
                ((Action<T>)sub)(value);
            }
        }

        // replicate to all connected clients if is server.
        BroadcastCvarChange(cvar, value);
    }

    private void BroadcastCvarChange<T>(CVarDef<T> cvar, T value)
        => BroadcastCvarChangeByName(cvar.Name, cvar, value!);

    private void BroadcastCvarChangeByName(string name, CVarDef def, object value)
    {
        if (def.Flags.HasFlag(CVar.REPLICATED) && _netMan.IsServer)
        {
            var serialized = FormatToml(value);
            _netMan.Broadcast(new MsgReplicateCvar(name, serialized));
        }
    }

    public bool TryGetByName(string name, out object? value)
        => _values.TryGetValue(name, out value);

    public bool TrySetByName(string name, string rawValue, out string? error)
    {
        if (!_defs.TryGetValue(name, out var def))
        {
            error = $"Unknown cvar: '{name}'";
            return false;
        }

        if (def.Flags.HasFlag(CVar.SERVER) && !_sharedContent.IsServer())
        {
            error = $"Cvar '{name}' can only be set by the server.";
            return false;
        }

        object value;
        try
        {
            value = ParseTomlValue(rawValue, def);
        }
        catch (Exception e)
        {
            error = $"Couldn't parse '{rawValue}' for cvar '{name}': {e.Message}";
            return false;
        }

        _values[name] = value;

        if (_subscribers.TryGetValue(name, out var subs))
            def.FireSubscribers(value, subs);

        BroadcastCvarChangeByName(name, def, value);

        error = null;
        return true;
    }

    public IEnumerable<string> AllCVarNames => _defs.Keys;

    public void Subs<T>(CVarDef<T> cvar, Action<T> callback, bool invokeImmediately = true)
    {
        var list = _subscribers.TryGetValue(cvar.Name, out var current)
            ? new List<Delegate>(current.Count + 1)
            : new List<Delegate>(1);

        if (current is not null)
            list.AddRange(current);

        list.Add(callback);
        _subscribers[cvar.Name] = list;

        if (invokeImmediately && _values.TryGetValue(cvar.Name, out var value))
            callback((T)value);
    }

    public void Unsubs<T>(CVarDef<T> cvar, Action<T> callback)
    {
        if (!_subscribers.TryGetValue(cvar.Name, out var current))
            return;

        var index = current.IndexOf(callback);
        if (index < 0)
            return;

        var list = new List<Delegate>(current);
        list.RemoveAt(index);
        _subscribers[cvar.Name] = list;
    }

    public void UnsubsAll(object owner)
    {
        foreach (var name in new List<string>(_subscribers.Keys))
        {
            var current = _subscribers[name];
            var list = current.FindAll(callback => !IsOwnedBy(callback, owner));
            if (list.Count != current.Count)
                _subscribers[name] = list;
        }
    }

    private static bool IsOwnedBy(Delegate callback, object owner)
    {
        foreach (var single in callback.GetInvocationList())
        {
            if (TargetReaches(single.Target, owner))
                return true;
        }

        return false;
    }

    // A lambda that captures locals doesnt point at the instance it was written in
    private static bool TargetReaches(object? target, object owner, int depth = 0)
    {
        if (target is null || depth > 8)
            return false;

        if (ReferenceEquals(target, owner))
            return true;

        var type = target.GetType();
        if (!type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            return false;

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.Name != "<>4__this" && !field.Name.StartsWith("CS$<>8__locals", StringComparison.Ordinal))
                continue;

            if (TargetReaches(field.GetValue(target), owner, depth + 1))
                return true;
        }

        return false;
    }

    public void SaveConfig()
    {
        var lines = new List<string>();

        foreach (var (name, value) in _values)
        {
            var def = _defs[name];

            if (def.Flags.HasFlag(CVar.NOSAVE))
                continue;

            var prop = def.GetType().GetProperty("DefaultValue");
            var defaultValue = prop?.GetValue(def);

            if (Equals(value, defaultValue))
                continue;

            lines.Add($"{name} = {FormatToml(value)}");
        }

        foreach (var (name, raw) in _unknownValues)
            lines.Add($"{name} = {raw}");

        _storage.WriteText("config.toml", string.Join("\n", lines));
    }

    public void LoadConfig()
    {
        var text = _storage.ReadText("config.toml");
        _unknownValues.Clear();

        if (text == null)
            return;

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split('=', 2);

            if (parts.Length != 2)
                continue;

            var name = parts[0].Trim();
            var raw = parts[1].Trim();

            if (!_defs.TryGetValue(name, out var def))
            {
                _unknownValues[name] = raw;
                continue;
            }

            var value = ParseTomlValue(raw, def);

            _values[name] = value;
            
            // update subscribers
            if (_subscribers.TryGetValue(name, out var list))
                def.FireSubscribers(value, list);
        }
        OnConfigLoad?.Invoke();
    }

    // TOML helpers: (i hate json)
    private static string FormatToml(object value)
    {
        return value switch
        {
            string s => $"\"{s}\"",
            bool b => b.ToString().ToLower(),
            float f => f.ToString(CultureInfo.InvariantCulture),
            _ => value.ToString()!
        };
    }

    // thanks gpt for create a simple toml parser and formarter
    private static object ParseTomlValue(string raw, CVarDef def)
    {
        var type = def.GetType();
        var generic = type.GenericTypeArguments[0];

        if (generic == typeof(string))
            return raw.Trim('"');

        if (generic == typeof(int))
            return int.Parse(raw);

        if (generic == typeof(bool))
            return bool.Parse(raw);

        if (generic == typeof(float))
            return float.Parse(raw, CultureInfo.InvariantCulture);

        return raw;
    }
    private static List<CVarDef> FindCVars()
    {
        var result = new List<CVarDef>();
        var assemblies = IoCManager.Resolve<SharedContentManager>().GetAssemblies();

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (!type.IsDefined(typeof(CVarDefsAttribute), false))
                    continue;

                var fields = type.GetFields(
                    BindingFlags.Public |
                    BindingFlags.Static |
                    BindingFlags.NonPublic);

                foreach (var field in fields)
                {
                    if (!typeof(CVarDef).IsAssignableFrom(field.FieldType))
                        continue;

                    var cvar = field.GetValue(null) as CVarDef;

                    if (cvar != null)
                        result.Add(cvar);
                }
            }
        }

        return result;
    }
}
