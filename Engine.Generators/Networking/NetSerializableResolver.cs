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
