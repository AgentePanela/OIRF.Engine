using System.IO;
using Engine.Shared.IoC;
using Engine.Shared.Serializer;
using Lidgren.Network;

namespace Engine.Shared.Networking;

/// <summary>
/// Fallback (de)serialization for a NetMessage field whose type isn't a
/// primitive or collection.
/// </summary>
public static class NetFallbackHelpers
{
    public static void Write<T>(NetOutgoingMessage buffer, T value)
    {
        using var ms = new MemoryStream();
        IoCManager.Resolve<ISerializationManager>().Serialize(ms, value!);
        buffer.WriteVariableInt32((int)ms.Length);
        buffer.Write(ms.GetBuffer(), 0, (int)ms.Length);
    }

    public static T Read<T>(NetIncomingMessage buffer)
    {
        var length = buffer.ReadVariableInt32();
        var bytes = buffer.ReadBytes(length);
        using var ms = new MemoryStream(bytes);
        return (T)IoCManager.Resolve<ISerializationManager>().Deserialize(ms);
    }
}
