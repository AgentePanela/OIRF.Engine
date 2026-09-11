using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Engine.Shared.Configuration;

[Flags]
public enum CVar : ushort
{
    NONE = 0,

    /// <summary>
    /// Changing this var syncs between clients and server.
    /// </summary>
    REPLICATED = 1 << 0,

    /// <summary>
    /// Only the server can edit this cvar.
    /// </summary>
    SERVER = 1 << 1,

    /// <summary>
    /// Only the client will register this cvar.
    /// </summary>
    CLIENTONLY = 1 << 2,

    /// <summary>
    /// Only the server will register this cvar.
    /// </summary>
    SERVERONLY = 1 << 3,

}

public abstract class CVarDef
{
    public string Name { get; }
    public CVar Flags { get; }

    protected CVarDef(string name, CVar flags)
    {
        Name = name;
        Flags = flags;
    }

    public static CVarDef<T> Create<T>(string name, T defaultValue, CVar flags = CVar.NONE)
    {
        return CVarDef<T>.Create(name, defaultValue, flags);
    }

    internal abstract void FireSubscribers(object value, IEnumerable<Delegate> subscribers);
}

public sealed class CVarDef<T> : CVarDef
{
    public T DefaultValue { get; internal set; }

    private CVarDef(string name, T defaultValue, CVar flags) : base(name, flags)
    {
        DefaultValue = defaultValue;
    }

    public static CVarDef<T> Create(string name, T defaultValue, CVar flags = CVar.NONE)
    {
        return new CVarDef<T>(name, defaultValue, flags);
    }

    internal override void FireSubscribers(object value, IEnumerable<Delegate> subscribers)
    {
        var typedValue = (T)value;
        foreach (var sub in subscribers)
        {
            ((Action<T>)sub)(typedValue);
        }
    }
}
