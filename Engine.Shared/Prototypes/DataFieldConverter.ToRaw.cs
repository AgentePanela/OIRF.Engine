using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace Engine.Shared.Prototypes;

public static partial class DataFieldConverter
{
    /// <summary>
    /// Inverse of <see cref="Convert(Type, object?)"/> - turns a live CLR value back into a
    /// plain, YAML-writable value (string/list/dict/primitive). Mirrors the same type-by-type
    /// dispatch <see cref="Convert(Type, object?)"/> uses, just in the opposite direction.
    /// </summary>
    public static object? ToRawValue(object? value)
    {
        if (value is null)
            return null;

        var type = value.GetType();

        if (type.IsEnum)
            return value.ToString()!;

        if (value is string || value is bool || value is decimal || type.IsPrimitive)
            return value;

        if (value is Microsoft.Xna.Framework.Vector2 v2)
            return $"{v2.X.ToString(CultureInfo.InvariantCulture)},{v2.Y.ToString(CultureInfo.InvariantCulture)}";

        if (value is Microsoft.Xna.Framework.Vector3 v3)
            return $"{v3.X.ToString(CultureInfo.InvariantCulture)},{v3.Y.ToString(CultureInfo.InvariantCulture)},{v3.Z.ToString(CultureInfo.InvariantCulture)}";

        if (value is Microsoft.Xna.Framework.Vector4 v4)
            return $"{v4.X.ToString(CultureInfo.InvariantCulture)},{v4.Y.ToString(CultureInfo.InvariantCulture)},{v4.Z.ToString(CultureInfo.InvariantCulture)},{v4.W.ToString(CultureInfo.InvariantCulture)}";

        if (value is Microsoft.Xna.Framework.Point pt)
            return $"{pt.X},{pt.Y}";

        if (value is Microsoft.Xna.Framework.Rectangle rect)
            return $"{rect.X},{rect.Y},{rect.Width},{rect.Height}";

        if (value is Microsoft.Xna.Framework.Color color)
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ProtoId<>))
            return value.ToString()!;

        // Collections BEFORE the TypeConverter fallback below - TypeDescriptor.GetConverter
        // returns a built-in System.ComponentModel.CollectionConverter for most collection
        // types (not just types with an explicit [TypeConverter] attribute), and that
        // converter's ConvertToString returns the literal, useless string "(Collection)".
        // Same ordering Convert(Type, object?) itself uses (collections before its own
        // TypeDescriptor fallback at the very end).
        if (value is IDictionary dict)
        {
            var result = new Dictionary<object, object>();
            foreach (DictionaryEntry entry in dict)
            {
                var rawValue = ToRawValue(entry.Value);
                if (rawValue is not null)
                    result[entry.Key] = rawValue;
            }

            return result;
        }

        if (value is IEnumerable seq)
        {
            var list = new List<object>();
            foreach (var item in seq)
            {
                var rawItem = ToRawValue(item);
                if (rawItem is not null)
                    list.Add(rawItem);
            }

            return list;
        }

        // Custom [TypeConverter] structs (ShaderPath, SpriteKey, ...) - anything with a
        // registered converter beyond the plain-ToString default one.
        var converter = TypeDescriptor.GetConverter(type);
        if (converter.GetType() != typeof(TypeConverter) && converter.CanConvertTo(typeof(string)))
            return converter.ConvertToString(null, CultureInfo.InvariantCulture, value);

        // Fallback for a nested complex object with no special handling above: recurse into its
        // own public settable properties, tagged with a "type" discriminator so Convert's own
        // complex-object path can resolve the concrete type back on load.
        if (type.GetConstructor(Type.EmptyTypes) is not null)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
            var result = new Dictionary<string, object> { ["type"] = type.Name };

            foreach (var prop in type.GetProperties(flags))
            {
                if (!prop.CanRead || prop.GetSetMethod(true) is null || prop.GetIndexParameters().Length > 0)
                    continue;

                var rawValue = ToRawValue(prop.GetValue(value));
                if (rawValue is not null)
                    result[prop.Name] = rawValue;
            }

            return result;
        }

        return value.ToString();
    }
}
