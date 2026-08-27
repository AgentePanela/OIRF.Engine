using System;
using System.Collections;
using System.Collections.Generic;

namespace Engine.Shared.GameObjects;

/// <summary>
/// A list of components and their entities UID.
/// </summary>
public readonly struct ComponentQuery<T> : IEnumerable<(EntityUid uid, T comp)> where T : Component
{
    private readonly Dictionary<EntityUid, Component>? _pool;

    internal ComponentQuery(Dictionary<EntityUid, Component>? pool) => _pool = pool;

    public Enumerator GetEnumerator() => new(_pool);

    IEnumerator<(EntityUid uid, T comp)> IEnumerable<(EntityUid uid, T comp)>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<(EntityUid uid, T comp)>
    {
        private Dictionary<EntityUid, Component>.Enumerator _inner;
        private readonly bool _hasPool;

        internal Enumerator(Dictionary<EntityUid, Component>? pool)
        {
            _hasPool = pool is not null;
            _inner = _hasPool ? pool!.GetEnumerator() : default;
        }

        public (EntityUid uid, T comp) Current
        {
            get
            {
                var kv = _inner.Current;
                return (kv.Key, (T)kv.Value);
            }
        }

        object IEnumerator.Current => Current;

        public bool MoveNext() => _hasPool && _inner.MoveNext();

        public void Reset() => throw new NotSupportedException();
        public void Dispose() { }
    }
}

/// <inheritdoc cref="ComponentQuery{T}"/>
public readonly struct ComponentQuery<T1, T2> : IEnumerable<(EntityUid uid, T1 comp1, T2 comp2)>
    where T1 : Component where T2 : Component
{
    private readonly Dictionary<EntityUid, Component>? _primary;
    private readonly Dictionary<EntityUid, Component>? _secondary;
    private readonly bool _primaryIsT1;

    internal ComponentQuery(Dictionary<EntityUid, Component>? pool1, Dictionary<EntityUid, Component>? pool2)
    {
        // walk whichever pool has fewer entities, probe the other
        if (pool1 is not null && pool2 is not null && pool2.Count < pool1.Count)
        {
            _primary = pool2;
            _secondary = pool1;
            _primaryIsT1 = false;
        }
        else
        {
            _primary = pool1;
            _secondary = pool2;
            _primaryIsT1 = true;
        }
    }

    public Enumerator GetEnumerator() => new(_primary, _secondary, _primaryIsT1);

    IEnumerator<(EntityUid uid, T1 comp1, T2 comp2)> IEnumerable<(EntityUid uid, T1 comp1, T2 comp2)>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<(EntityUid uid, T1 comp1, T2 comp2)>
    {
        private Dictionary<EntityUid, Component>.Enumerator _inner;
        private readonly Dictionary<EntityUid, Component>? _secondary;
        private readonly bool _primaryIsT1;
        private readonly bool _hasPools;
        private EntityUid _uid;
        private Component? _primaryComp;
        private Component? _secondaryComp;

        internal Enumerator(Dictionary<EntityUid, Component>? primary, Dictionary<EntityUid, Component>? secondary, bool primaryIsT1)
        {
            _hasPools = primary is not null && secondary is not null;
            _inner = _hasPools ? primary!.GetEnumerator() : default;
            _secondary = secondary;
            _primaryIsT1 = primaryIsT1;
            _uid = default;
            _primaryComp = null;
            _secondaryComp = null;
        }

        public (EntityUid uid, T1 comp1, T2 comp2) Current
            => _primaryIsT1
                ? (_uid, (T1)_primaryComp!, (T2)_secondaryComp!)
                : (_uid, (T1)_secondaryComp!, (T2)_primaryComp!);

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (!_hasPools)
                return false;

            while (_inner.MoveNext())
            {
                var kv = _inner.Current;
                if (!_secondary!.TryGetValue(kv.Key, out var other))
                    continue;

                _uid = kv.Key;
                _primaryComp = kv.Value;
                _secondaryComp = other;
                return true;
            }

            return false;
        }

        public void Reset() => throw new NotSupportedException();
        public void Dispose() { }
    }
}

/// <inheritdoc cref="ComponentQuery{T}"/>
public readonly struct ComponentQuery<T1, T2, T3> : IEnumerable<(EntityUid uid, T1 comp1, T2 comp2, T3 comp3)>
    where T1 : Component where T2 : Component where T3 : Component
{
    private readonly Dictionary<EntityUid, Component>? _p1;
    private readonly Dictionary<EntityUid, Component>? _p2;
    private readonly Dictionary<EntityUid, Component>? _p3;
    private readonly Dictionary<EntityUid, Component>? _primary;

    internal ComponentQuery(
        Dictionary<EntityUid, Component>? pool1,
        Dictionary<EntityUid, Component>? pool2,
        Dictionary<EntityUid, Component>? pool3)
    {
        _p1 = pool1;
        _p2 = pool2;
        _p3 = pool3;

        if (pool1 is null || pool2 is null || pool3 is null)
        {
            _primary = null;
            return;
        }

        // walk whichever pool has fewer entities, probe the other two
        _primary = pool1;
        if (pool2.Count < _primary.Count) _primary = pool2;
        if (pool3.Count < _primary.Count) _primary = pool3;
    }

    public Enumerator GetEnumerator() => new(_p1, _p2, _p3, _primary);

    IEnumerator<(EntityUid uid, T1 comp1, T2 comp2, T3 comp3)> IEnumerable<(EntityUid uid, T1 comp1, T2 comp2, T3 comp3)>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<(EntityUid uid, T1 comp1, T2 comp2, T3 comp3)>
    {
        private Dictionary<EntityUid, Component>.Enumerator _inner;
        private readonly Dictionary<EntityUid, Component>? _p1;
        private readonly Dictionary<EntityUid, Component>? _p2;
        private readonly Dictionary<EntityUid, Component>? _p3;
        private readonly bool _hasPools;
        private EntityUid _uid;
        private Component? _c1, _c2, _c3;

        internal Enumerator(
            Dictionary<EntityUid, Component>? p1,
            Dictionary<EntityUid, Component>? p2,
            Dictionary<EntityUid, Component>? p3,
            Dictionary<EntityUid, Component>? primary)
        {
            _p1 = p1;
            _p2 = p2;
            _p3 = p3;
            _hasPools = primary is not null;
            _inner = _hasPools ? primary!.GetEnumerator() : default;
            _uid = default;
            _c1 = _c2 = _c3 = null;
        }

        public (EntityUid uid, T1 comp1, T2 comp2, T3 comp3) Current
            => (_uid, (T1)_c1!, (T2)_c2!, (T3)_c3!);

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (!_hasPools)
                return false;

            while (_inner.MoveNext())
            {
                var uid = _inner.Current.Key;
                if (!_p1!.TryGetValue(uid, out var c1) ||
                    !_p2!.TryGetValue(uid, out var c2) ||
                    !_p3!.TryGetValue(uid, out var c3))
                    continue;

                _uid = uid;
                _c1 = c1;
                _c2 = c2;
                _c3 = c3;
                return true;
            }

            return false;
        }

        public void Reset() => throw new NotSupportedException();
        public void Dispose() { }
    }
}
