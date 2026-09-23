namespace Engine.Shared.Networking;

/// <summary>
/// How the message should be delivery though network
/// </summary>
public enum NetDeliveryMethod
{
    /// <summary>
    /// Fire and forget: can arrive out of order, or not at all.
    /// </summary>
    Unreliable,

    /// <summary>
    /// Unreliable, but an older message arriving after a newer one is dropped.
    /// </summary>
    UnreliableSequenced,

    /// <summary>
    /// Guaranteed to arrive, but order isn't guaranteed.
    /// </summary>
    ReliableUnordered,

    /// <summary>
    /// Guaranteed to arrive - an older message arriving after a newer one is dropped.
    /// </summary>
    ReliableSequenced,

    /// <summary>
    /// Guaranteed to arrive, in the order it was sent.
    /// </summary>
    ReliableOrdered,
}

public static class NetDeliveryMethodExtensions
{

    private const int MaxUnreliableBits = 65535;

    private static bool _warnedAboutSize;

    /// <summary>
    /// Same as <see cref="ToLidgren(NetDeliveryMethod)"/>, but a message too big to go unreliable is sent reliably
    /// instead.
    /// </summary>
    public static Lidgren.Network.NetDeliveryMethod ToLidgren(this NetDeliveryMethod method, int sizeBits)
    {
        if (sizeBits <= MaxUnreliableBits || method is not (NetDeliveryMethod.Unreliable or NetDeliveryMethod.UnreliableSequenced))
            return method.ToLidgren();

        if (!_warnedAboutSize)
        {
            _warnedAboutSize = true;
            Log.Warn($"A {method} message of {sizeBits} bits does not fit a packet (max {MaxUnreliableBits}), sending it reliably. Further ones are not logged.");
        }

        return Lidgren.Network.NetDeliveryMethod.ReliableOrdered;
    }

    public static Lidgren.Network.NetDeliveryMethod ToLidgren(this NetDeliveryMethod method) => method switch
    {
        NetDeliveryMethod.Unreliable => Lidgren.Network.NetDeliveryMethod.Unreliable,
        NetDeliveryMethod.UnreliableSequenced => Lidgren.Network.NetDeliveryMethod.UnreliableSequenced,
        NetDeliveryMethod.ReliableUnordered => Lidgren.Network.NetDeliveryMethod.ReliableUnordered,
        NetDeliveryMethod.ReliableSequenced => Lidgren.Network.NetDeliveryMethod.ReliableSequenced,
        NetDeliveryMethod.ReliableOrdered => Lidgren.Network.NetDeliveryMethod.ReliableOrdered,
        _ => throw new System.ArgumentOutOfRangeException(nameof(method), method, null),
    };
}
