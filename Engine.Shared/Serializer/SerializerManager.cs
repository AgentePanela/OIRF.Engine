using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Engine.Shared.Serializer.TypeSerializers;
using NetSerializer;

namespace Engine.Shared.Serializer;

internal sealed class SerializerManager : ISerializationManager
{
    private NetSerializer.Serializer? _serializer;
    private List<Type> _types = new();
    public List<ITypeSerializer> CustomSerializers { get; set; } = new() { new MonoGameTypeSerializer() };

    public void Init(IEnumerable<Assembly> assemblies)
    {
        if (_serializer is not null)
            throw new InvalidOperationException("SerializerManager is already initialized.");

        /*
            i dont like this....
            this one uses runtime reflection, maybe we can use MemoryPack.Core
            to implement serialization in compile-time in the Engine.Generator
            https://github.com/Cysharp/MemoryPack/tree/main/src/MemoryPack.Core
        */
#pragma warning disable SYSLIB0050
        _types = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsSerializable
                && !t.IsAbstract
                && !t.ContainsGenericParameters
                && !t.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
                && !typeof(Delegate).IsAssignableFrom(t)
                && !typeof(System.Runtime.Serialization.ISerializable).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();
#pragma warning restore SYSLIB0050

        var settings = new Settings
        {
            CustomTypeSerializers = CustomSerializers.ToArray(),
        };

        _serializer = new NetSerializer.Serializer(_types, settings);

        Log.Debug($"Serializer hash: {GetHash()}");
    }

    public void Serialize(Stream stream, object message)
        => Instance.Serialize(stream, message);

    public object Deserialize(Stream stream)
        => Instance.Deserialize(stream);

    public string GetHash()
        => Instance.GetSHA256();

    public void SelfTest()
    {
        foreach (var type in _types)
        {
            object blank;
            try
            {
                // bypasses every constructor
                blank = RuntimeHelpers.GetUninitializedObject(type);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"SerializerManager: couldn't construct a blank '{type.FullName}' to test with.", ex);
            }

            try
            {
                using var ms = new MemoryStream();
                Instance.Serialize(ms, blank);
                ms.Position = 0;
                var result = Instance.Deserialize(ms);

                if (result is null || result.GetType() != type)
                    throw new InvalidOperationException($"Returned '{result?.GetType().FullName ?? "null"}', expected '{type.FullName}'.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"SerializerManager self-test failed for message type '{type.FullName}'.", ex);
            }
        }
    }

    private NetSerializer.Serializer Instance
        => _serializer ?? throw new InvalidOperationException("SerializerManager is not inited!");
}
