using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NetSerializer;

namespace Engine.Shared.Serializer.TypeSerializers;

internal sealed class HashSetTypeSerializer : IStaticTypeSerializer
{
    public bool Handles(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>);

    public IEnumerable<Type> GetSubtypes(Type type)
        => new[] { type.GetGenericArguments()[0] };

    public MethodInfo GetStaticWriter(Type type)
        => typeof(HashSetTypeSerializer).GetMethod(nameof(Write), BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(type.GetGenericArguments()[0]);

    public MethodInfo GetStaticReader(Type type)
        => typeof(HashSetTypeSerializer).GetMethod(nameof(Read), BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(type.GetGenericArguments()[0]);

    public static void Write<T>(NetSerializer.Serializer serializer, Stream stream, HashSet<T>? value)
    {
        Primitives.WritePrimitive(stream, (uint)(value?.Count ?? 0));
        if (value is null)
            return;

        foreach (var item in value)
            serializer.Serialize(stream, item!);
    }

    public static void Read<T>(NetSerializer.Serializer serializer, Stream stream, out HashSet<T> value)
    {
        Primitives.ReadPrimitive(stream, out uint count);

        value = new HashSet<T>((int)count);
        for (var i = 0; i < count; i++)
            value.Add((T)serializer.Deserialize(stream));
    }
}
