using System;
using System.Collections.Generic;

namespace Engine.Shared.Configuration;

public abstract class CVarDef
{
    public string Name { get; }

    protected CVarDef(string name)
    {
        Name = name;
    }

    public static CVarDef<T> Create<T>(string name, T defaultValue)
    {
        return CVarDef<T>.Create(name, defaultValue);
    }

    internal abstract void FireSubscribers(object value, IEnumerable<Delegate> subscribers);
}

public sealed class CVarDef<T> : CVarDef
{
    public T DefaultValue { get; internal set; }

    private CVarDef(string name, T defaultValue) : base(name)
    {
        DefaultValue = defaultValue;
    }

    public static CVarDef<T> Create(string name, T defaultValue)
    {
        return new CVarDef<T>(name, defaultValue);
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
