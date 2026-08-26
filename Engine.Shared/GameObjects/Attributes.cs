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
/// Controls this system's relative order for Init/Update/Draw/Shutdown against every other
/// system - lower runs first. Systems without this attribute default to priority 0. Systems
/// that tie (including two systems both left at the default) keep whatever order they'd
/// otherwise register in.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SystemPriorityAttribute(int priority) : Attribute
{
    public int Priority => priority;
}
