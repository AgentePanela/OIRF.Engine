using System;

namespace Engine.Shared.Networking;

/// <summary>
/// Excludes a public settable property from a NetMessage's generated
/// WriteToBuffer/ReadFromBuffer.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class NetIgnoreAttribute : Attribute { }

/// <summary>
/// Marks a type as safe to be serializabled on a networked message.
/// </summary>
// [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
// public sealed class NetSerializableAttribute : Attribute
// {
// }