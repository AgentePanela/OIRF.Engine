using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NetSerializer;

namespace Engine.Shared.Serializer;

/// <summary>
/// Handles class networking serialization.
/// </summary>
public interface ISerializationManager
{
    /// <summary>
    /// Used to define custom serializers before the SerializationManager init.
    /// </summary>
    public List<ITypeSerializer> CustomSerializers { get; set; }

    void Init(IEnumerable<Assembly> assemblies);

    void Serialize(Stream stream, object message);

    object Deserialize(Stream stream);

    void SelfTest();

    /// <summary>
    /// Hash identifying the exact type map the manager built.
    /// </summary>
    string GetHash();
}
