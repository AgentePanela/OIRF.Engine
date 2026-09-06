using System;

namespace Engine.Shared.Networking;

// [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
// public sealed class NetFieldAttribute : Attribute
// {
// }

/// <summary>
/// Makes the message builder ignore that property. Making it not networked.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class NetIgnoreAttribute : Attribute
{
}