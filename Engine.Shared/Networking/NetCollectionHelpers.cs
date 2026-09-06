using System;
using System.Collections.Generic;
using Lidgren.Network;

namespace Engine.Shared.Networking;

/// <summary>
/// Read/Write helpers for collections like List and Dicts.
/// </summary>
public static class NetCollectionHelpers
{
    public static void WriteCollection<T>(NetOutgoingMessage buffer, IReadOnlyCollection<T> items, Action<NetOutgoingMessage, T> writeItem)
    {
        buffer.Write(items.Count);
        foreach (var item in items)
            writeItem(buffer, item);
    }

    public static List<T> ReadList<T>(NetIncomingMessage buffer, Func<NetIncomingMessage, T> readItem)
    {
        var count = buffer.ReadInt32();
        var list = new List<T>(count);
        for (var i = 0; i < count; i++)
            list.Add(readItem(buffer));
        return list;
    }

    public static T[] ReadArray<T>(NetIncomingMessage buffer, Func<NetIncomingMessage, T> readItem)
    {
        var count = buffer.ReadInt32();
        var array = new T[count];
        for (var i = 0; i < count; i++)
            array[i] = readItem(buffer);
        return array;
    }

    public static void WriteDictionary<TKey, TValue>(NetOutgoingMessage buffer, IReadOnlyDictionary<TKey, TValue> dict,
        Action<NetOutgoingMessage, TKey> writeKey, Action<NetOutgoingMessage, TValue> writeValue) where TKey : notnull
    {
        buffer.Write(dict.Count);
        foreach (var kvp in dict)
        {
            writeKey(buffer, kvp.Key);
            writeValue(buffer, kvp.Value);
        }
    }

    public static Dictionary<TKey, TValue> ReadDictionary<TKey, TValue>(NetIncomingMessage buffer,
        Func<NetIncomingMessage, TKey> readKey, Func<NetIncomingMessage, TValue> readValue) where TKey : notnull
    {
        var count = buffer.ReadInt32();
        var dict = new Dictionary<TKey, TValue>(count);
        for (var i = 0; i < count; i++)
        {
            var key = readKey(buffer);
            var value = readValue(buffer);
            dict.Add(key, value);
        }
        return dict;
    }
}
