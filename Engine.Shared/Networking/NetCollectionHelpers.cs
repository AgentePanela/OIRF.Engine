using System;
using System.Collections.Generic;
using Lidgren.Network;

namespace Engine.Shared.Networking;

/// <summary>
/// Read/Write helpers for collections like List and Dicts.
/// </summary>
public static class NetCollectionHelpers
{
    public static void WriteCollection<T>(NetOutgoingMessage buffer, IReadOnlyCollection<T> collection, Action<NetOutgoingMessage, T> writeItem)
    {
        buffer.WriteVariableInt32(collection.Count);
        foreach (var item in collection)
            writeItem(buffer, item);
    }

    public static List<T> ReadList<T>(NetIncomingMessage buffer, Func<NetIncomingMessage, T> readItem)
    {
        var count = buffer.ReadVariableInt32();
        var list = new List<T>(count);
        for (var i = 0; i < count; i++)
            list.Add(readItem(buffer));

        return list;
    }

    public static T[] ReadArray<T>(NetIncomingMessage buffer, Func<NetIncomingMessage, T> readItem)
    {
        var count = buffer.ReadVariableInt32();
        var array = new T[count];
        for (var i = 0; i < count; i++)
            array[i] = readItem(buffer);

        return array;
    }

    public static void WriteDictionary<TKey, TValue>(NetOutgoingMessage buffer, IReadOnlyDictionary<TKey, TValue> dictionary, Action<NetOutgoingMessage, TKey> writeKey, Action<NetOutgoingMessage, TValue> writeValue) where TKey : notnull
    {
        buffer.WriteVariableInt32(dictionary.Count);
        foreach (var (key, value) in dictionary)
        {
            writeKey(buffer, key);
            writeValue(buffer, value);
        }
    }

    public static Dictionary<TKey, TValue> ReadDictionary<TKey, TValue>(NetIncomingMessage buffer, Func<NetIncomingMessage, TKey> readKey, Func<NetIncomingMessage, TValue> readValue) where TKey : notnull
    {
        var count = buffer.ReadVariableInt32();
        var dictionary = new Dictionary<TKey, TValue>(count);
        for (var i = 0; i < count; i++)
        {
            var key = readKey(buffer);
            var value = readValue(buffer);
            dictionary[key] = value;
        }

        return dictionary;
    }
}
