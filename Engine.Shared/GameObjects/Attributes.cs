using System;

namespace Engine.Shared.GameObjects;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RegisterComponentAttribute(string name) : Attribute
{
    public string Name => name;
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
