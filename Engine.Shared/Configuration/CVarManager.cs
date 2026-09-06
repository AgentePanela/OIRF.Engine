using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
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
    /// Save all cvars that have the current values different from the default ones to the dataPath/config.toml
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
    {
        if (cvar.Flags.HasFlag(CVar.REPLICATED) && _netMan.IsServer)
        {
            var serialized = FormatToml(value!);
            foreach (var session in _netMan.Sessions)
                session.SendMessage(new MsgReplicateCvar(cvar.Name, serialized));
        }
    }

    public void Subs<T>(CVarDef<T> cvar, Action<T> callback, bool invokeImmediately = true)
    {
        if (!_subscribers.TryGetValue(cvar.Name, out var list))
        {
            list = new List<Delegate>();
            _subscribers[cvar.Name] = list;
        }

        list.Add(callback);

        if (invokeImmediately && _values.TryGetValue(cvar.Name, out var value))
            callback((T)value);
    }

    public void SaveConfig()
    {
        var lines = new List<string>();

        foreach (var (name, value) in _values)
        {
            var def = _defs[name];

            var prop = def.GetType().GetProperty("DefaultValue");
            var defaultValue = prop?.GetValue(def);

            if (Equals(value, defaultValue))
                continue;

            lines.Add($"{name} = {FormatToml(value)}");
        }

        _storage.WriteText("config.toml", string.Join("\n", lines));
    }

    public void LoadConfig()
    {
        var text = _storage.ReadText("config.toml");

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
                continue;

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
