using System;
using Engine.Shared.Prototypes;

/// <summary>
/// The unique ID a entity can have. Used as indentifier to almost every ECS function.
/// </summary>
[Serializable]
public readonly struct EntityUid : IEquatable<EntityUid>, IComparable<EntityUid>, ISpanFormattable
{
    public readonly int Id;

    /// <summary>
    /// An Invalid entity UID you can compare against.
    /// </summary>
    public static readonly EntityUid Empty = new(-1);

    public static bool operator ==(EntityUid l, EntityUid r) => l.Id == r.Id;
    public static bool operator !=(EntityUid l, EntityUid r) => l.Id != r.Id;

    public EntityUid(int id)
    {
        Id = id;
    }

    /// <summary>
    /// Verify if the entityUid is null or have a invalid uid.
    /// </summary>
    // todo: verify in entity manager for the valid uid.
    public static bool IsInvalid(EntityUid? uid)
    {
        return uid == null || uid.Value.Id == -1;
    }

    public int CompareTo(EntityUid other)
    {
        return Id.CompareTo(other.Id);
    }

    public bool Equals(EntityUid other)
    {
        return Id == other.Id;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj))
            return false;
        return obj is EntityUid id && Equals(id);
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        return Id.ToString();
    }

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        return Id.TryFormat(destination, out charsWritten);
    }

    /// <summary>
    /// Creates an entity UID by parsing a string number.
    /// </summary>
    public static EntityUid Parse(ReadOnlySpan<char> uid)
    {
        return new EntityUid(int.Parse(uid));
    }

    public static bool TryParse(ReadOnlySpan<char> uid, out EntityUid entityUid)
    {
        if (!int.TryParse(uid, out var id))
        {
            entityUid = default;
            return false;
        }

        entityUid = new(id);
        return true;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            //idk
            return Id.GetHashCode() * 397;
        }
    }
}

/// <summary>
/// The ID of an entity over the network. Used by networking to recognise a netid into a entity uid
/// </summary>
[Serializable]
public readonly struct NetEntity : IEquatable<NetEntity>, IComparable<NetEntity>, ISpanFormattable
{
    public readonly int Id;

    /// <summary>
    /// An Invalid net entity you can compare against. It is also default.
    /// </summary>
    public static readonly NetEntity Invalid = new(0);

    public static bool operator ==(NetEntity l, NetEntity r) => l.Id == r.Id;
    public static bool operator !=(NetEntity l, NetEntity r) => l.Id != r.Id;

    public NetEntity(int id)
    {
        Id = id;
    }

    public bool IsValid => Id > 0;

    public int CompareTo(NetEntity other) => Id.CompareTo(other.Id);

    public bool Equals(NetEntity other) => Id == other.Id;

    public override bool Equals(object? obj) => obj is NetEntity other && Equals(other);

    public override int GetHashCode() => Id;

    public override string ToString() => Id.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Id.ToString();

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        => Id.TryFormat(destination, out charsWritten);
}

[Serializable]
public readonly record struct ProtoId(string Value);

[Serializable]
public readonly struct ProtoId<T> : IEquatable<ProtoId<T>> where T : IPrototype
{
    public readonly string Id;

    public ProtoId(string id)
    {
        Id = id;
    }

    public override string ToString() => Id;

    public bool Equals(ProtoId<T> other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is ProtoId<T> other && Equals(other);
    public override int GetHashCode() => Id?.GetHashCode() ?? 0;

    public static bool operator ==(ProtoId<T> left, ProtoId<T> right) => left.Equals(right);
    public static bool operator !=(ProtoId<T> left, ProtoId<T> right) => !left.Equals(right);

    public static implicit operator string(ProtoId<T> id) => id.Id;
    public static implicit operator ProtoId<T>(string id) => new(id);
}
