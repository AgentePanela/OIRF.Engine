using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Engine.Shared.GameObjects;
using Engine.Shared.Prototypes;

namespace Engine.Shared.Debug.ViewVariables;

public static class ViewVariablesConvert
{
    private const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.Instance;

    public readonly record struct VVMemberDesc(MemberInfo Member, bool CanWrite);

    private static readonly ConcurrentDictionary<Type, VVMemberDesc[]> _memberCache = new();

    public static VVMemberDesc[] ScanMembers(Type type)
    {
        if (_memberCache.TryGetValue(type, out var cached))
            return cached;

        var excludeDeclaringType = typeof(Component).IsAssignableFrom(type) ? typeof(Component) : typeof(object);

        var props = type.GetProperties(MemberFlags)
            .Where(p => p.DeclaringType != excludeDeclaringType && p.CanRead && p.GetIndexParameters().Length == 0)
            .Where(p => p.GetCustomAttribute<ViewVariablesHiddenAttribute>() is null)
            .Select(p => new VVMemberDesc(p,
                p.GetSetMethod(true) is not null && p.GetCustomAttribute<ViewVariablesReadOnlyAttribute>() is null));

        var fields = type.GetFields(MemberFlags)
            .Where(f => f.DeclaringType != excludeDeclaringType && !f.IsLiteral)
            .Where(f => f.GetCustomAttribute<ViewVariablesHiddenAttribute>() is null)
            .Select(f => new VVMemberDesc(f,
                !f.IsInitOnly && f.GetCustomAttribute<ViewVariablesReadOnlyAttribute>() is null));

        var result = props.Concat(fields)
            .OrderBy(m => m.Member.Name, StringComparer.Ordinal)
            .ToArray();

        _memberCache[type] = result;
        return result;
    }

    public static string ToText(object? value)
    {
        if (value is null)
            return "null";

        // ToRawValue would dump this as "{Id: 5}" otherwise
        if (value is EntityUid uid)
            return uid.Id.ToString();

        if (value is IDictionary dict)
            return $"{{{dict.Count}}}";

        if (value is IEnumerable seq && value is not string)
        {
            var count = 0;
            foreach (var _ in seq)
                count++;

            return $"[{count}]";
        }

        try
        {
            return DataFieldConverter.ToRawValue(value)?.ToString() ?? "null";
        }
        catch (Exception ex)
        {
            return $"<error: {ex.Message}>";
        }
    }

    public static bool TryParse(Type targetType, string text, out object? value, out string? error)
    {
        error = null;

        var underlying = Nullable.GetUnderlyingType(targetType);
        if (underlying is not null)
        {
            if (text.Length == 0 || string.Equals(text, "null", StringComparison.OrdinalIgnoreCase))
            {
                value = null;
                return true;
            }

            targetType = underlying;
        }

        // DataFieldConverter.Convert doesn't handle EntityUid
        if (targetType == typeof(EntityUid))
        {
            if (text.Length == 0 || string.Equals(text, "null", StringComparison.OrdinalIgnoreCase))
            {
                value = EntityUid.Empty;
                return true;
            }

            if (!EntityUid.TryParse(text, out var uid))
            {
                value = null;
                error = $"'{text}' is not a valid entity uid.";
                return false;
            }

            value = uid;
            return true;
        }

        try
        {
            value = DataFieldConverter.Convert(targetType, text);
            return true;
        }
        catch (Exception ex)
        {
            value = null;
            error = ex.Message;
            return false;
        }
    }
}
