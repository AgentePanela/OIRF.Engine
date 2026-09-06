using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Engine.Generators.Networking;

/// <summary>
/// Figures out how to write/read a single type - a message property's type,
/// or (recursively) a field's type inside a [NetSerializable] type. Doesn't
/// know anything about messages/classes as a whole - that's NetMessageGenerator's job.
/// </summary>
internal static class NetSerializableResolver
{
    public const string SerializableAttributeFullName = "Engine.Shared.Networking.NetSerializableAttribute";
    public const string IgnoreAttributeFullName = "Engine.Shared.Networking.NetIgnoreAttribute";

    private static readonly DiagnosticDescriptor UnsupportedType = new(
        id: Diagnostics.NetFieldUnsupportedTypeID,
        title: "Unsupported INetMessage property type",
        messageFormat: "Type {0} in {1} isn't serializable - try marking it [NetSerializable] or write WriteToBuffer/ReadFromBuffer by hand instead",
        category: "Engine.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NoMatchingCtor = new(
        id: Diagnostics.NoMatchingNetSerializableCtorID,
        title: "No matching constructor for [NetSerializable] type",
        messageFormat: "'{0}' is [NetSerializable] but has no public constructor whose parameter names match its public fields ({1}) - property '{2}' can't be (de)serialized",
        category: "Engine.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor Circular = new(
        id: Diagnostics.CircularNetSerializableID,
        title: "Circular [NetSerializable] reference",
        messageFormat: "'{0}' (used by property '{1}') contains itself, directly or through another [NetSerializable] type - can't be (de)serialized",
        category: "Engine.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// What to emit for one resolved property/field: the expression that reads
    /// it back, and the (possibly multi-line, from nested [NetSerializable]
    /// fields) statements that write it.
    /// </summary>
    public sealed class FieldPlan
    {
        public readonly string ReadExpr;
        public readonly List<string> WriteLines;

        public FieldPlan(string readExpr, List<string> writeLines)
        {
            ReadExpr = readExpr;
            WriteLines = writeLines;
        }
    }

    public static bool HasAttribute(ISymbol symbol, string attributeFullName)
        => symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == attributeFullName);

    public static FieldPlan? Resolve(ITypeSymbol type, string accessPath, HashSet<ITypeSymbol> visiting, Location? location, SourceProductionContext spc)
    {
        var typeName = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();

        var primitiveMethod = GetReadMethod(typeName);
        if (primitiveMethod is not null)
            return new FieldPlan($"buffer.{primitiveMethod}()", new List<string> { $"buffer.Write({accessPath});" });

        var collectionPlan = TryResolveCollection(type, accessPath, visiting, location, spc, out var isCollection);
        if (isCollection)
            return collectionPlan; // null means the element type already reported its own diagnostic

        var dictionaryPlan = TryResolveDictionary(type, accessPath, visiting, location, spc, out var isDictionary);
        if (isDictionary)
            return dictionaryPlan;

        if (!HasAttribute(type, SerializableAttributeFullName))
        {
            spc.ReportDiagnostic(Diagnostic.Create(UnsupportedType, location ?? Location.None, typeName, accessPath));
            return null;
        }

        if (!visiting.Add(type))
        {
            spc.ReportDiagnostic(Diagnostic.Create(Circular, location ?? Location.None, typeName, accessPath));
            return null;
        }

        var namedType = type as INamedTypeSymbol;
        var innerFields = (namedType?.GetMembers().OfType<IFieldSymbol>() ?? Enumerable.Empty<IFieldSymbol>())
            .Where(f => !f.IsStatic && f.DeclaredAccessibility == Accessibility.Public)
            .Where(f => !HasAttribute(f, IgnoreAttributeFullName))
            .ToList();

        var ctor = namedType?.InstanceConstructors.FirstOrDefault(c =>
            c.DeclaredAccessibility == Accessibility.Public &&
            c.Parameters.Length == innerFields.Count &&
            c.Parameters.All(p => innerFields.Any(f => string.Equals(f.Name, p.Name, StringComparison.OrdinalIgnoreCase))));

        if (ctor is null)
        {
            visiting.Remove(type);
            var fieldNames = innerFields.Count == 0 ? "none" : string.Join(", ", innerFields.Select(f => f.Name));
            spc.ReportDiagnostic(Diagnostic.Create(NoMatchingCtor, location ?? Location.None, typeName, fieldNames, accessPath));
            return null;
        }

        // Constructor parameter order is the canonical order for both write and read
        var writeLines = new List<string>();
        var readArgs = new List<string>();

        foreach (var param in ctor.Parameters)
        {
            var field = innerFields.First(f => string.Equals(f.Name, param.Name, StringComparison.OrdinalIgnoreCase));
            var subPlan = Resolve(field.Type, $"{accessPath}.{field.Name}", visiting, location, spc);
            if (subPlan is null)
            {
                visiting.Remove(type);
                return null;
            }

            writeLines.AddRange(subPlan.WriteLines);
            readArgs.Add(subPlan.ReadExpr);
        }

        visiting.Remove(type);
        return new FieldPlan($"new {typeName}({string.Join(", ", readArgs)})", writeLines);
    }

    /// <summary>
    /// List&lt;T&gt; or T[]: resolves the element type T (recursively - same
    /// rules as anything else) and wraps it in a call to NetCollectionHelpers,
    /// which is what actually keeps this a single expression on the read
    /// side - required so a collection can be nested as a constructor
    /// argument inside another [NetSerializable] type, not just live directly
    /// on a message.
    /// </summary>
    private static FieldPlan? TryResolveCollection(ITypeSymbol type, string accessPath, HashSet<ITypeSymbol> visiting, Location? location, SourceProductionContext spc, out bool isCollection)
    {
        ITypeSymbol elementType;
        string readHelper;

        if (type is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
            readHelper = "ReadArray";
        }
        else if (type is INamedTypeSymbol { TypeArguments.Length: 1 } namedType &&
                 namedType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>")
        {
            elementType = namedType.TypeArguments[0];
            readHelper = "ReadList";
        }
        else
        {
            isCollection = false;
            return null;
        }

        isCollection = true;
        var elementPlan = Resolve(elementType, "item", visiting, location, spc);
        if (elementPlan is null)
            return null;

        var elementTypeName = elementType.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();
        var writeBody = string.Join(" ", elementPlan.WriteLines);

        var writeLine = $"global::Engine.Shared.Networking.NetCollectionHelpers.WriteCollection(buffer, {accessPath}, (buffer, item) => {{ {writeBody} }});";
        var readExpr = $"global::Engine.Shared.Networking.NetCollectionHelpers.{readHelper}<{elementTypeName}>(buffer, buffer => {elementPlan.ReadExpr})";

        return new FieldPlan(readExpr, new List<string> { writeLine });
    }

    /// <summary>
    /// Dictionary&lt;TKey, TValue&gt;: same idea as TryResolveCollection, just
    /// resolving both the key and value types and wrapping them in their own
    /// lambdas passed to NetCollectionHelpers.WriteDictionary/ReadDictionary.
    /// </summary>
    private static FieldPlan? TryResolveDictionary(ITypeSymbol type, string accessPath, HashSet<ITypeSymbol> visiting, Location? location, SourceProductionContext spc, out bool isDictionary)
    {
        if (type is not INamedTypeSymbol { TypeArguments.Length: 2 } namedType ||
            namedType.OriginalDefinition.ToDisplayString() != "System.Collections.Generic.Dictionary<TKey, TValue>")
        {
            isDictionary = false;
            return null;
        }

        isDictionary = true;
        var keyType = namedType.TypeArguments[0];
        var valueType = namedType.TypeArguments[1];

        var keyPlan = Resolve(keyType, "key", visiting, location, spc);
        if (keyPlan is null)
            return null;

        var valuePlan = Resolve(valueType, "value", visiting, location, spc);
        if (valuePlan is null)
            return null;

        var keyTypeName = keyType.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();
        var valueTypeName = valueType.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();
        var keyWriteBody = string.Join(" ", keyPlan.WriteLines);
        var valueWriteBody = string.Join(" ", valuePlan.WriteLines);

        var writeLine = $"global::Engine.Shared.Networking.NetCollectionHelpers.WriteDictionary(buffer, {accessPath}, " +
            $"(buffer, key) => {{ {keyWriteBody} }}, (buffer, value) => {{ {valueWriteBody} }});";
        var readExpr = $"global::Engine.Shared.Networking.NetCollectionHelpers.ReadDictionary<{keyTypeName}, {valueTypeName}>(buffer, " +
            $"buffer => {keyPlan.ReadExpr}, buffer => {valuePlan.ReadExpr})";

        return new FieldPlan(readExpr, new List<string> { writeLine });
    }

    // Every supported primitive shares the same Write() overload - only Read differs per type.
    private static string? GetReadMethod(string typeName) => typeName switch
    {
        "bool" => "ReadBoolean",
        "byte" => "ReadByte",
        "sbyte" => "ReadSByte",
        "short" => "ReadInt16",
        "ushort" => "ReadUInt16",
        "int" => "ReadInt32",
        "uint" => "ReadUInt32",
        "long" => "ReadInt64",
        "ulong" => "ReadUInt64",
        "float" => "ReadSingle",
        "double" => "ReadDouble",
        "string" => "ReadString",
        "System.Net.IPEndPoint" => "ReadIPEndPoint",
        _ => null,
    };
}
