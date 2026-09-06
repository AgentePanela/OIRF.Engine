using System;

namespace Engine.Shared.Networking;

// [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
// public sealed class NetFieldAttribute : Attribute
// {
// }

/// <summary>
/// Makes the message builder ignore that property/field. Making it not networked.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class NetIgnoreAttribute : Attribute
{
}

/// <summary>
/// Marks a type as safe to be serializabled on a networked message.
/// </summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class NetSerializableAttribute : Attribute
{
}