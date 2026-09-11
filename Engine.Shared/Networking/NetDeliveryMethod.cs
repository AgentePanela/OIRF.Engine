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
