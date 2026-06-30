using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Engine.Shared.Prototypes;

public static partial class DataFieldConverter
{
    private static readonly ConcurrentDictionary<(string, Type), Type?> _typeResolutionCache = new();
    
    /// <summary>
    /// Convert a raw YAML-parsed value to the specified target CLR type.
    /// Supports: primitives, enums, string, Guid, DateTime, DateTimeOffset, TimeSpan,
    /// Uri, string[], T[], List&lt;T&gt;, IList&lt;T&gt;, IReadOnlyList&lt;T&gt;,
    /// Collection&lt;T&gt;, ICollection&lt;T&gt;, IEnumerable&lt;T&gt;,
    /// HashSet&lt;T&gt;, SortedSet&lt;T&gt;, ISet&lt;T&gt;, IReadOnlySet&lt;T&gt;,
    /// Queue&lt;T&gt;, Stack&lt;T&gt;, LinkedList&lt;T&gt;,
    /// Dictionary&lt;K,V&gt;, SortedDictionary&lt;K,V&gt;, SortedList&lt;K,V&gt;,
    /// IDictionary&lt;K,V&gt;, IReadOnlyDictionary&lt;K,V&gt;,
    /// ProtoId&lt;T&gt;, Vector2, Vector3, Point, Rectangle, Color (XNA),
    /// Tuple&lt;...&gt;, ValueTuple&lt;...&gt;,
    /// complex objects with parameterless constructors (recursive).
    /// </summary>
    public static object? Convert(Type targetType, object? rawValue)
    {
        if (rawValue is null)
            return null;
 
        // Unwrap Nullable<T> — treat as the underlying type from here on.
        targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        var rawType = rawValue.GetType();
 
        // ── Already the correct type ─────────────────────────────────────
        if (targetType.IsAssignableFrom(rawType))
            return rawValue;
 
        // ── string ───────────────────────────────────────────────────────
        if (targetType == typeof(string))
            return rawValue.ToString();
 
        var str = rawValue.ToString()!;
 
        // ── Enums ─────────────────────────────────────────────────────────
        // Must come before IConvertible — Enum implements IConvertible but
        // Convert.ChangeType won't parse names.
        if (targetType.IsEnum)
            return Enum.Parse(targetType, str, ignoreCase: true);
 
        // ── Guid ──────────────────────────────────────────────────────────
        if (targetType == typeof(Guid))
            return Guid.Parse(str);
 
        // ── DateTime / DateTimeOffset / TimeSpan ──────────────────────────
        if (targetType == typeof(DateTime))
            return DateTime.Parse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
 
        if (targetType == typeof(DateTimeOffset))
            return DateTimeOffset.Parse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
 
        if (targetType == typeof(TimeSpan))
            return TimeSpan.Parse(str, CultureInfo.InvariantCulture);
 
        // ── Uri ───────────────────────────────────────────────────────────
        if (targetType == typeof(Uri))
            return new Uri(str, UriKind.RelativeOrAbsolute);
 
        // ── IConvertible primitives (int, float, bool, long, decimal, …) ──
        if (typeof(IConvertible).IsAssignableFrom(targetType))
            return System.Convert.ChangeType(str, targetType, CultureInfo.InvariantCulture);
 
        // ── XNA / MonoGame value types ────────────────────────────────────
        if (targetType == typeof(Microsoft.Xna.Framework.Vector2))
            return ParseVector2(rawValue);
 
        if (targetType == typeof(Microsoft.Xna.Framework.Vector3))
            return ParseVector3(rawValue);
 
        if (targetType == typeof(Microsoft.Xna.Framework.Vector4))
            return ParseVector4(rawValue);
 
        if (targetType == typeof(Microsoft.Xna.Framework.Point))
            return ParsePoint(rawValue);
 
        if (targetType == typeof(Microsoft.Xna.Framework.Rectangle))
            return ParseRectangle(rawValue);
 
        if (targetType == typeof(Microsoft.Xna.Framework.Color))
            return ParseColor(rawValue, str);
 
        // ── ProtoId<T> ────────────────────────────────────────────────────
        if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(ProtoId<>))
            return Activator.CreateInstance(targetType, str);
 
        // ══ COLLECTION TYPES ══════════════════════════════════════════════
        // Resolve element type and a flat sequence of converted elements for
        // any IEnumerable<object> source, then materialise as needed.
 
        if (targetType.IsGenericType)
        {
            var gtd = targetType.GetGenericTypeDefinition();
            var typeArgs = targetType.GetGenericArguments();
 
            // ── Dictionary variants ───────────────────────────────────────
            if (IsDictionaryDefinition(gtd) && rawValue is IDictionary rawDict)
                return BuildDictionary(targetType, gtd, typeArgs, rawDict);
 
            // ── Array T[] ─────────────────────────────────────────────────
            // (generic array is not possible, handled below in the non-generic section)
 
            // ── Sequence collections with one type argument ───────────────
            if (typeArgs.Length == 1 && rawValue is IEnumerable<object> seq)
                return BuildSequenceCollection(targetType, gtd, typeArgs[0], seq);
 
            // ── Tuple<T1…T8> ──────────────────────────────────────────────
            if (IsTupleDefinition(gtd))
                return BuildTuple(targetType, typeArgs, rawValue);
 
            // ── ValueTuple<T1…T8> ─────────────────────────────────────────
            if (IsValueTupleDefinition(gtd))
                return BuildValueTuple(targetType, typeArgs, rawValue);
        }
 
        // ── Non-generic or closed array (string[], int[], …) ─────────────
        if (targetType == typeof(string[]))
        {
            if (rawValue is string s)
                return new[] { s };
            if (rawValue is IEnumerable<object> list)
                return list.Select(x => x.ToString()!).ToArray();
        }
 
        if (targetType.IsArray)
        {
            var elemType = targetType.GetElementType()!;
            if (rawValue is IEnumerable<object> seqArr)
            {
                var items = seqArr.Select(x => Convert(elemType, x)).ToArray();
                var arr = Array.CreateInstance(elemType, items.Length);
                for (var i = 0; i < items.Length; i++)
                    arr.SetValue(items[i], i);
                return arr;
            }
        }
 
        // ── Complex objects with a parameterless constructor ──────────────
        // A YAML mapping may carry a "type" key naming the concrete subtype,
        // which is required when the field's declared type is abstract or an
        // interface (e.g. CollisionShape > BoxShape).
        //
        // Resolution order:
        //   1. Exact class name  (case-sensitive)
        //   2. Class name        (case-insensitive)
        //   Works across ALL loaded assemblies - no attributes needed.
        if (rawValue is Dictionary<string, object> objDict && !targetType.IsPrimitive)
        {
            var concreteType = targetType;
 
            if (objDict.TryGetValue("type", out var typeTag) && typeTag is string typeName)
            {
                concreteType = ResolveConcreteType(typeName, targetType)
                    ?? throw new PrototypeLoadException(
                        $"Cannot resolve concrete type '{typeName}' for base '{targetType.Name}'. " +
                        $"Make sure a class named '{typeName}' exists and inherits/implements '{targetType.Name}'.");
 
                // Remove the discriminator key before populating fields.
                objDict = new Dictionary<string, object>(objDict);
                objDict.Remove("type");
            }
 
            if (concreteType.IsAbstract || concreteType.IsInterface)
                throw new PrototypeLoadException(
                    $"Cannot instantiate abstract type '{concreteType.Name}'. " +
                    $"Add a 'type:' key in the YAML to specify the concrete subtype.");
 
            if (concreteType.GetConstructor(Type.EmptyTypes) is null)
                throw new PrototypeLoadException(
                    $"Type '{concreteType.Name}' has no parameterless constructor.");
 
            var instance         = Activator.CreateInstance(concreteType)!;
            var dataFieldMembers = GetDataFieldMembers(concreteType);
 
            if (dataFieldMembers.Length > 0)
                ApplyFields(instance, objDict);
            else
                ApplyByName(instance, objDict);
 
            return instance;
        }
 
        // ── TypeDescriptor fallback (registered TypeConverters) ───────────
        var converter = TypeDescriptor.GetConverter(targetType);
        if (converter.CanConvertFrom(rawType))
            return converter.ConvertFrom(null, CultureInfo.InvariantCulture, rawValue)!;
 
        throw new PrototypeLoadException(
            $"Cannot convert '{rawValue}' ({rawType.Name}) to {targetType.Name}.");
    }
 
    // ═══════════════════════════════════════════════════════════════════════
    //  Collection builders
    // ═══════════════════════════════════════════════════════════════════════
 
    private static bool IsDictionaryDefinition(Type gtd) =>
        gtd == typeof(Dictionary<,>)
        || gtd == typeof(SortedDictionary<,>)
        || gtd == typeof(SortedList<,>)
        || gtd == typeof(IDictionary<,>)
        || gtd == typeof(IReadOnlyDictionary<,>);
 
    private static object BuildDictionary(Type targetType, Type gtd, Type[] typeArgs, IDictionary rawDict)
    {
        var keyType   = typeArgs[0];
        var valueType = typeArgs[1];
 
        // Interfaces → use Dictionary<K,V> as concrete type.
        Type concreteType = (gtd == typeof(IDictionary<,>) || gtd == typeof(IReadOnlyDictionary<,>))
            ? typeof(Dictionary<,>).MakeGenericType(keyType, valueType)
            : targetType;
 
        var dict = (IDictionary)Activator.CreateInstance(concreteType)!;
 
        foreach (DictionaryEntry entry in rawDict)
            dict.Add(Convert(keyType, entry.Key)!, Convert(valueType, entry.Value));
 
        return dict;
    }
 
    /// <summary>
    /// Builds any single-element-type collection from a YAML sequence.
    /// Supported generic type definitions: List&lt;&gt;, IList&lt;&gt;,
    /// IReadOnlyList&lt;&gt;, Collection&lt;&gt;, ICollection&lt;&gt;,
    /// IEnumerable&lt;&gt;, IReadOnlyCollection&lt;&gt;, HashSet&lt;&gt;,
    /// SortedSet&lt;&gt;, ISet&lt;&gt;, IReadOnlySet&lt;&gt;,
    /// Queue&lt;&gt;, Stack&lt;&gt;, LinkedList&lt;&gt;.
    /// </summary>
    private static object BuildSequenceCollection(Type targetType, Type gtd, Type elemType, IEnumerable<object> seq)
    {
        var converted = seq.Select(x => Convert(elemType, x)).ToList();
 
        // ── Set types ────────────────────────────────────────────────────
        if (gtd == typeof(HashSet<>)
            || gtd == typeof(ISet<>)
#if NET5_0_OR_GREATER
            || gtd == typeof(IReadOnlySet<>)
#endif
        )
        {
            var hs = (IEnumerable)Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(elemType))!;
            var addMethod = hs.GetType().GetMethod("Add")!;
            foreach (var item in converted)
                addMethod.Invoke(hs, new[] { item });
            return hs;
        }
 
        if (gtd == typeof(SortedSet<>))
        {
            var ss = (IEnumerable)Activator.CreateInstance(typeof(SortedSet<>).MakeGenericType(elemType))!;
            var addMethod = ss.GetType().GetMethod("Add")!;
            foreach (var item in converted)
                addMethod.Invoke(ss, new[] { item });
            return ss;
        }
 
        // ── Queue<T> ─────────────────────────────────────────────────────
        if (gtd == typeof(Queue<>))
        {
            var q = (IEnumerable)Activator.CreateInstance(typeof(Queue<>).MakeGenericType(elemType))!;
            var enqueueMethod = q.GetType().GetMethod("Enqueue")!;
            foreach (var item in converted)
                enqueueMethod.Invoke(q, new[] { item });
            return q;
        }
 
        // ── Stack<T> ─────────────────────────────────────────────────────
        if (gtd == typeof(Stack<>))
        {
            var stk = (IEnumerable)Activator.CreateInstance(typeof(Stack<>).MakeGenericType(elemType))!;
            var pushMethod = stk.GetType().GetMethod("Push")!;
            // Preserve YAML order (first item at bottom of stack).
            foreach (var item in converted)
                pushMethod.Invoke(stk, new[] { item });
            return stk;
        }
 
        // ── LinkedList<T> ─────────────────────────────────────────────────
        if (gtd == typeof(LinkedList<>))
        {
            var ll = (IEnumerable)Activator.CreateInstance(typeof(LinkedList<>).MakeGenericType(elemType))!;
            var addLastMethod = ll.GetType().GetMethod("AddLast", new[] { elemType })!;
            foreach (var item in converted)
                addLastMethod.Invoke(ll, new[] { item });
            return ll;
        }
 
        // ── List<T> and all list-like interfaces ──────────────────────────
        // IList<>, IReadOnlyList<>, ICollection<>, IReadOnlyCollection<>,
        // IEnumerable<>, Collection<>, List<>  → all backed by List<T>.
        if (gtd == typeof(List<>)
            || gtd == typeof(IList<>)
            || gtd == typeof(IReadOnlyList<>)
            || gtd == typeof(ICollection<>)
            || gtd == typeof(IReadOnlyCollection<>)
            || gtd == typeof(IEnumerable<>)
            || gtd == typeof(Collection<>))
        {
            var list = (IList)Activator.CreateInstance(
                (gtd == typeof(Collection<>))
                    ? typeof(Collection<>).MakeGenericType(elemType)
                    : typeof(List<>).MakeGenericType(elemType))!;
            foreach (var item in converted)
                list.Add(item);
            return list;
        }
 
        // ── Unknown generic with IList constructor ─────────────────────────
        // Last resort: try to construct the concrete type directly.
        if (typeof(IList).IsAssignableFrom(targetType)
            && targetType.GetConstructor(Type.EmptyTypes) is not null)
        {
            var list = (IList)Activator.CreateInstance(targetType)!;
            foreach (var item in converted)
                list.Add(item);
            return list;
        }
 
        throw new PrototypeLoadException(
            $"Cannot build collection of type '{targetType.Name}' from a YAML sequence.");
    }
 
    // ═══════════════════════════════════════════════════════════════════════
    //  Tuple builders
    // ═══════════════════════════════════════════════════════════════════════
 
    private static readonly HashSet<Type> _tupleDefinitions = new()
    {
        typeof(Tuple<>), typeof(Tuple<,>), typeof(Tuple<,,>), typeof(Tuple<,,,>),
        typeof(Tuple<,,,,>), typeof(Tuple<,,,,,>), typeof(Tuple<,,,,,,>), typeof(Tuple<,,,,,,,>)
    };
 
    private static readonly HashSet<Type> _valueTupleDefinitions = new()
    {
        typeof(ValueTuple<>), typeof(ValueTuple<,>), typeof(ValueTuple<,,>), typeof(ValueTuple<,,,>),
        typeof(ValueTuple<,,,,>), typeof(ValueTuple<,,,,,>), typeof(ValueTuple<,,,,,,>), typeof(ValueTuple<,,,,,,,>)
    };
 
    private static bool IsTupleDefinition(Type gtd)      => _tupleDefinitions.Contains(gtd);
    private static bool IsValueTupleDefinition(Type gtd) => _valueTupleDefinitions.Contains(gtd);
 
    private static object BuildTuple(Type targetType, Type[] typeArgs, object rawValue)
    {
        var items = ExtractTupleItems(typeArgs, rawValue);
        // Tuple.Create accepts up to 8 params; use the right overload via reflection.
        var createMethod = typeof(Tuple).GetMethods()
            .First(m => m.Name == "Create" && m.GetParameters().Length == typeArgs.Length)
            .MakeGenericMethod(typeArgs);
        return createMethod.Invoke(null, items)!;
    }
 
    private static object BuildValueTuple(Type targetType, Type[] typeArgs, object rawValue)
    {
        var items = ExtractTupleItems(typeArgs, rawValue);
        // ValueTuple is a struct — use its constructor directly.
        var ctor = targetType.GetConstructor(typeArgs)
            ?? throw new PrototypeLoadException($"No matching ValueTuple constructor for {targetType.Name}.");
        return ctor.Invoke(items);
    }
 
    private static object?[] ExtractTupleItems(Type[] typeArgs, object rawValue)
    {
        IList<object?> elements;
 
        if (rawValue is IEnumerable<object> seq)
            elements = seq.Cast<object?>().ToList();
        else if (rawValue is string s)
            elements = s.Split(',', StringSplitOptions.TrimEntries).Cast<object?>().ToList();
        else
            throw new PrototypeLoadException(
                $"Cannot build Tuple from raw value of type '{rawValue.GetType().Name}'.");
 
        if (elements.Count != typeArgs.Length)
            throw new PrototypeLoadException(
                $"Tuple expects {typeArgs.Length} elements but got {elements.Count}.");
 
        return elements.Select((e, i) => Convert(typeArgs[i], e)).ToArray();
    }
 
    // ═══════════════════════════════════════════════════════════════════════
    //  XNA / MonoGame parsers
    // ═══════════════════════════════════════════════════════════════════════
 
    private static float[] ExtractFloats(object rawValue, int expected, string typeName)
    {
        float[] result;
 
        if (rawValue is IEnumerable<object> seq)
        {
            result = seq.Select(x => float.Parse(x.ToString()!, CultureInfo.InvariantCulture)).ToArray();
        }
        else
        {
            var parts = rawValue.ToString()!
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            result = parts.Select(p => float.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        }
 
        if (result.Length != expected)
            throw new PrototypeLoadException(
                $"Invalid {typeName}: expected {expected} values, got {result.Length}. Input: '{rawValue}'");
 
        return result;
    }
 
    private static int[] ExtractInts(object rawValue, int expected, string typeName)
    {
        int[] result;
 
        if (rawValue is IEnumerable<object> seq)
        {
            result = seq.Select(x => int.Parse(x.ToString()!, CultureInfo.InvariantCulture)).ToArray();
        }
        else
        {
            var parts = rawValue.ToString()!
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            result = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        }
 
        if (result.Length != expected)
            throw new PrototypeLoadException(
                $"Invalid {typeName}: expected {expected} values, got {result.Length}. Input: '{rawValue}'");
 
        return result;
    }
 
    private static float[] SplitFloats(string str, int expected, string typeName)
        => ExtractFloats(str, expected, typeName);
 
    private static int[] SplitInts(string str, int expected, string typeName)
        => ExtractInts(str, expected, typeName);
 
    private static Microsoft.Xna.Framework.Vector2 ParseVector2(object rawValue)
    {
        var f = ExtractFloats(rawValue, 2, "Vector2");
        return new Microsoft.Xna.Framework.Vector2(f[0], f[1]);
    }
 
    private static Microsoft.Xna.Framework.Vector3 ParseVector3(object rawValue)
    {
        var f = ExtractFloats(rawValue, 3, "Vector3");
        return new Microsoft.Xna.Framework.Vector3(f[0], f[1], f[2]);
    }
 
    private static Microsoft.Xna.Framework.Vector4 ParseVector4(object rawValue)
    {
        var f = ExtractFloats(rawValue, 4, "Vector4");
        return new Microsoft.Xna.Framework.Vector4(f[0], f[1], f[2], f[3]);
    }
 
    private static Microsoft.Xna.Framework.Point ParsePoint(object rawValue)
    {
        var i = ExtractInts(rawValue, 2, "Point");
        return new Microsoft.Xna.Framework.Point(i[0], i[1]);
    }
 
    private static Microsoft.Xna.Framework.Rectangle ParseRectangle(object rawValue)
    {
        var i = ExtractInts(rawValue, 4, "Rectangle");
        return new Microsoft.Xna.Framework.Rectangle(i[0], i[1], i[2], i[3]);
    }
 
    /// <summary>
    /// Accepts:
    ///   • "#RRGGBB" / "#RRGGBBAA"   hex strings
    ///   • "r,g,b" / "r,g,b,a"      byte triples/quads
    ///   • "R:r G:g B:b A:a"         XNA ToString() format
    ///   • named colors via reflection (Microsoft.Xna.Framework.Color.CornflowerBlue, etc.)
    /// </summary>
    private static Microsoft.Xna.Framework.Color ParseColor(object rawValue, string str)
    {
        str = str.Trim();
 
        // Hex: #RGB, #RRGGBB, #RRGGBBAA
        if (str.StartsWith('#'))
        {
            var hex = str[1..];
            return hex.Length switch
            {
                3  => new Microsoft.Xna.Framework.Color(
                         HexToByte(hex[0], hex[0]),
                         HexToByte(hex[1], hex[1]),
                         HexToByte(hex[2], hex[2])),
                6  => new Microsoft.Xna.Framework.Color(
                         HexToByte(hex[0], hex[1]),
                         HexToByte(hex[2], hex[3]),
                         HexToByte(hex[4], hex[5])),
                8  => new Microsoft.Xna.Framework.Color(
                         HexToByte(hex[0], hex[1]),
                         HexToByte(hex[2], hex[3]),
                         HexToByte(hex[4], hex[5]),
                         HexToByte(hex[6], hex[7])),
                _ => throw new PrototypeLoadException($"Invalid hex color: '{str}'.")
            };
        }
 
        // "r,g,b" or "r,g,b,a"
        if (str.Contains(','))
        {
            var parts = str.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 3)
                return new Microsoft.Xna.Framework.Color(
                    byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2]));
            if (parts.Length == 4)
                return new Microsoft.Xna.Framework.Color(
                    byte.Parse(parts[0]), byte.Parse(parts[1]),
                    byte.Parse(parts[2]), byte.Parse(parts[3]));
        }
 
        // "R:255 G:128 B:0 A:255" (XNA Color.ToString() format)
        if (str.StartsWith("R:", StringComparison.OrdinalIgnoreCase))
        {
            byte r = 0, g = 0, b = 0, a = 255;
            foreach (var token in str.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = token.Split(':');
                if (kv.Length != 2) continue;
                var channel = byte.Parse(kv[1], CultureInfo.InvariantCulture);
                switch (kv[0].ToUpperInvariant())
                {
                    case "R": r = channel; break;
                    case "G": g = channel; break;
                    case "B": b = channel; break;
                    case "A": a = channel; break;
                }
            }
            return new Microsoft.Xna.Framework.Color(r, g, b, a);
        }
 
        // Named color — e.g. "CornflowerBlue", "White", "Red"
        var colorProp = typeof(Microsoft.Xna.Framework.Color)
            .GetProperty(str, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
        if (colorProp is not null)
            return (Microsoft.Xna.Framework.Color)colorProp.GetValue(null)!;
 
        throw new PrototypeLoadException(
            $"Cannot parse Color from '{str}'. Expected #hex, 'r,g,b[,a]', 'R:n G:n B:n A:n', or a named color.");
    }
 
    private static byte HexToByte(char hi, char lo) =>
        System.Convert.ToByte($"{hi}{lo}", 16);

    /// <summary>
    /// Finds a concrete CLR type by name that is assignable to <paramref name="baseType"/>.
    /// Searches all loaded assemblies. No attributes needed — matches by class name.
    /// </summary>
    public static Type? ResolveConcreteType(string typeName, Type baseType)
    {
        var key = (typeName, baseType);
        if (_typeResolutionCache.TryGetValue(key, out var cached))
            return cached;
 
        Type? exactMatch = null;
        Type? fuzzyMatch = null;
 
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[]? types;
            try   { types = asm.GetTypes(); }
            catch { continue; }
 
            foreach (var t in types)
            {
                if (t.IsAbstract || t.IsInterface || !baseType.IsAssignableFrom(t))
                    continue;
 
                if (string.Equals(t.Name, typeName, StringComparison.Ordinal))
                {
                    exactMatch = t;
                    break; // exact match wins immediately
                }
 
                if (fuzzyMatch is null
                    && string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase))
                    fuzzyMatch = t;
            }
 
            if (exactMatch is not null) break;
        }
 
        var result = exactMatch ?? fuzzyMatch;
        _typeResolutionCache[key] = result;
        return result;
    }
}
