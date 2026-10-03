using System;

namespace Engine.Shared.GameObjects;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RegisterComponentAttribute(string name) : Attribute
{
    public string Name => name;
}

/// <summary>
/// Marks a component as replicated from the server to the clients. Via networking.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NetworkedComponentAttribute : Attribute
{
}

/// <summary>
/// Marks a property/field of a <see cref="NetworkedComponentAttribute"/> component to be replicated.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true)]
public sealed class NetFieldAttribute : Attribute
{
}

/// <summary>
/// Make the property/field or component auto dirty when the setter of one NetField is called.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public sealed class AutoDirtyAttribute : Attribute
{
}

/// <summary>
/// Which writers <see cref="IgnoreAttribute"/> applies to.
/// </summary>
[Flags]
public enum IgnoreIn
{
    /// <summary>
    /// e.g: content map saving.
    /// </summary>
    Serialization = 1 << 0,

    /// <summary>
    /// Entity cloning (<see cref="EntityManager.CloneEntity"/>).
    /// </summary>
    Clone = 1 << 1,

    All = Serialization | Clone,
}

/// <summary>
/// Keeps a component member out of the writers in <see cref="IgnoreIn"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true)]
public sealed class IgnoreAttribute(IgnoreIn @in = IgnoreIn.All) : Attribute
{
    public IgnoreIn In => @in;
}

/// <summary>
/// Makes EntityManager.Systems ignore this system registry during loading.<para/>
/// This also makes the system do not registry a IoC container or parent registry. Even if it is abstracted.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class IgnoreSystemRegistryAttribute() : Attribute
{
}

/// <summary>
/// Controls this system priority order for Init/Update/Draw/Shutdown against every other
/// system.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SystemPriorityAttribute(int priority) : Attribute
{
    public int Priority => priority;
}