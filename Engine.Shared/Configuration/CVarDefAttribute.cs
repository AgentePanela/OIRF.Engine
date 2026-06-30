using System;

namespace Engine.Shared.Configuration;

/// <summary>
/// Marks the class as a cvar def class, this will be used by <see cref="IConfigurationManager"/> to load cvars from this class.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CVarDefsAttribute : Attribute
{
}
