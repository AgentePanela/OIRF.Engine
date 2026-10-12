using System;

namespace Engine.Shared.Extensions;

public static class EnumExtensions
{
    public static T Next<T>(this T value) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        var index = Array.IndexOf(values, value);
        return values[(index + 1) % values.Length];
    }
}