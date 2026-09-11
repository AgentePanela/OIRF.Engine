using System.Linq;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Engine.Generators.Networking;

/// <summary>
/// Figures out how to write/read a single type.
/// </summary>
internal static class NetSerializableResolver
{
    public const string IgnoreAttributeFullName = "Engine.Shared.Networking.NetIgnoreAttribute";
    private const string SerializableAttributeFullName = "System.SerializableAttribute";

    // Keep in sync with Engine.Shared.Serializer.TypeSerializers
    private static readonly HashSet<string> KnownCustomSerializedTypes = new()
    {
        "Microsoft.Xna.Framework.Vector2",
        "Microsoft.Xna.Framework.Vector3",
        "Microsoft.Xna.Framework.Vector4",
        "Microsoft.Xna.Framework.Point",
        "Microsoft.Xna.Framework.Rectangle",
        "Microsoft.Xna.Framework.Color",
    };

    private static readonly DiagnosticDescriptor UnsupportedType = new(
        id: Diagnostics.NetFieldUnsupportedTypeID,
        title: "Unsupported NetMessage property type",
        messageFormat: "Type {0} in {1} isn't a primitive/collection and isn't marked [Serializable] - mark it [Serializable] so NetSerializer can handle it, or write WriteToBuffer/ReadFromBuffer by hand instead",
        category: "Engine.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// What to emit for one resolved property/field
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

    /// <summary>
    /// Symbol names come back from Roslyn without their '@' (a property
    /// declared as "@class" has Name == "class"
    /// </summary>
    public static string EscapeIdentifier(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    public static FieldPlan? Resolve(ITypeSymbol type, string accessPath, Location? location, SourceProductionContext spc)
    {
        // Nullable<T> (int?, EntityUid?)
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableValueType)
            return ResolveNullable(nullableValueType.TypeArguments[0], accessPath, location, spc, isValueType: true);

        // Nullable reference type (string?, SomeClass?)
        if (type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated)
            return ResolveNullable(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated), accessPath, location, spc, isValueType: false);

        var typeName = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();

        var primitive = GetPrimitiveMethods(typeName);
        if (primitive is { } p)
            return new FieldPlan($"buffer.{p.Read}()", new List<string> { $"buffer.{p.Write}({accessPath});" });

        var collectionPlan = TryResolveCollection(type, accessPath, location, spc, out var isCollection);
        if (isCollection)
            return collectionPlan; // null means the element type already reported its own diagnostic

        var dictionaryPlan = TryResolveDictionary(type, accessPath, location, spc, out var isDictionary);
        if (isDictionary)
            return dictionaryPlan;

        // Anything else falls back to NetSerializer, which requires [Serializable]
        if (!HasAttribute(type, SerializableAttributeFullName) && !KnownCustomSerializedTypes.Contains(typeName))
        {
            spc.ReportDiagnostic(Diagnostic.Create(UnsupportedType, location ?? Location.None, typeName, accessPath));
            return null;
        }

        var writeLine = $"global::Engine.Shared.Networking.NetFallbackHelpers.Write(buffer, {accessPath});";
        var readExpr = $"global::Engine.Shared.Networking.NetFallbackHelpers.Read<{typeName}>(buffer)";
        return new FieldPlan(readExpr, new List<string> { writeLine });
    }

    private static FieldPlan? ResolveNullable(ITypeSymbol underlying, string accessPath, Location? location, SourceProductionContext spc, bool isValueType)
    {
        var valueAccessPath = isValueType ? $"{accessPath}.Value" : accessPath;
        var innerPlan = Resolve(underlying, valueAccessPath, location, spc);
        if (innerPlan is null)
            return null;

        var hasValueCheck = isValueType ? $"{accessPath}.HasValue" : $"{accessPath} is not null";
        var writeBody = string.Join("\n            ", innerPlan.WriteLines);
        var writeLines = new List<string>
        {
            $"buffer.Write({hasValueCheck});",
            $"if ({hasValueCheck})",
            "{",
            $"    {writeBody}",
            "}",
        };

        var underlyingName = underlying.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();
        var castPrefix = isValueType ? $"({underlyingName}?)" : "";
        var readExpr = $"(buffer.ReadBoolean() ? {castPrefix}{innerPlan.ReadExpr} : null)";

        return new FieldPlan(readExpr, writeLines);
    }

    private static FieldPlan? TryResolveCollection(ITypeSymbol type, string accessPath, Location? location, SourceProductionContext spc, out bool isCollection)
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
        var elementPlan = Resolve(elementType, "item", location, spc);
        if (elementPlan is null)
            return null;

        var elementTypeName = elementType.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();
        var writeBody = string.Join(" ", elementPlan.WriteLines);

        var writeLine = $"global::Engine.Shared.Networking.NetCollectionHelpers.WriteCollection(buffer, {accessPath}, (buffer, item) => {{ {writeBody} }});";
        var readExpr = $"global::Engine.Shared.Networking.NetCollectionHelpers.{readHelper}<{elementTypeName}>(buffer, buffer => {elementPlan.ReadExpr})";

        return new FieldPlan(readExpr, new List<string> { writeLine });
    }

    private static FieldPlan? TryResolveDictionary(ITypeSymbol type, string accessPath, Location? location, SourceProductionContext spc, out bool isDictionary)
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

        var keyPlan = Resolve(keyType, "key", location, spc);
        if (keyPlan is null)
            return null;

        var valuePlan = Resolve(valueType, "value", location, spc);
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

    // Most primitives share the plain Write() overload (Lidgren dispatches by
    // parameter type)
    private static (string Write, string Read)? GetPrimitiveMethods(string typeName) => typeName switch
    {
        "bool" => ("Write", "ReadBoolean"),
        "byte" => ("Write", "ReadByte"),
        "sbyte" => ("Write", "ReadSByte"),
        "short" => ("Write", "ReadInt16"),
        "ushort" => ("Write", "ReadUInt16"),
        "int" => ("WriteVariableInt32", "ReadVariableInt32"),
        "uint" => ("WriteVariableUInt32", "ReadVariableUInt32"),
        "long" => ("WriteVariableInt64", "ReadVariableInt64"),
        "ulong" => ("WriteVariableUInt64", "ReadVariableUInt64"),
        "float" => ("Write", "ReadSingle"),
        "double" => ("Write", "ReadDouble"),
        "string" => ("Write", "ReadString"),
        "System.Net.IPEndPoint" => ("Write", "ReadIPEndPoint"),
        _ => null,
    };
}
