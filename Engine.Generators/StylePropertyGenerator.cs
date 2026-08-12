using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;

namespace Engine.Generators;

[Generator]
public sealed class StylePropertyGenerator : IIncrementalGenerator
{
    private const string AttributeFullName = "Engine.Client.UI.StyleFieldAttribute";

    private static readonly DiagnosticDescriptor NoMatchingConstructor = new(
        id: Diagnostics.StyleConstructorID,
        title: "No matching constructor for [StyleField] default",
        messageFormat: "'{0}' has no public constructor matching the default given to [StyleField] on '{1}'",        category: "Engine.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var fields = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeFullName,
                predicate: static (node, _) => node is VariableDeclaratorSyntax,
                transform: static (ctx, _) => Extract(ctx))
            .Where(static f => f is not null)
            .Select(static (f, _) => f!);

        context.RegisterSourceOutput(fields.Collect(), static (spc, allFields) =>
        {
            foreach (var group in allFields.GroupBy(f => (f.Namespace, f.ClassName)))
            {
                var errors = group.Where(f => f.ErrorLocation is not null);
                foreach (var error in errors)
                    spc.ReportDiagnostic(Diagnostic.Create(NoMatchingConstructor, error.ErrorLocation, error.UnderlyingType, error.FieldName));

                var ok = group.Where(f => f.ErrorLocation is null).ToList();
                if (ok.Count == 0)
                    continue;

                var hintName = group.Key.Namespace.Length == 0
                    ? $"{group.Key.ClassName}.g.cs"
                    : $"{group.Key.Namespace}.{group.Key.ClassName}.g.cs";

                spc.AddSource(hintName, GenerateClass(group.Key.Namespace, group.Key.ClassName, ok));
            }
        });
    }

    private static FieldInfo? Extract(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not IFieldSymbol field)
            return null;

        var attr = field.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == AttributeFullName);

        if (attr is null || attr.ConstructorArguments.Length == 0)
            return null;

        if (attr.ConstructorArguments[0].Value is not string styleName)
            return null;

        // "_minWidth" -> "MinWidth"
        var fieldName = field.Name;
        var propertyName = fieldName.Length > 1 && fieldName[0] == '_'
            ? char.ToUpperInvariant(fieldName[1]) + fieldName.Substring(2)
            : char.ToUpperInvariant(fieldName[0]) + fieldName.Substring(1);

        var fieldType = field.Type is INamedTypeSymbol { ConstructedFrom.SpecialType: SpecialType.System_Nullable_T } named
            ? named.TypeArguments[0]
            : field.Type;

        // A nullable value type (Thickness?) unwraps above to its clean, non-nullable
        // TypeArguments[0] - but a nullable REFERENCE type (string?) is still the same String
        // symbol, just annotated, and ToDisplayString() renders that annotation as a trailing
        // "?" on its own. Strip it explicitly so Generate() doesn't end up appending a second
        // one (isNullable fields get their own "?" tacked on there) and emitting "string??".
        var underlyingType = fieldType.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();

        string? defaultLiteral = null;
        Location? errorLocation = null;

        if (attr.ConstructorArguments.Length > 1)
        {
            var (literal, failed) = FormatLiteral(attr.ConstructorArguments[1], fieldType);
            if (failed)
                errorLocation = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation();
            else
                defaultLiteral = literal;
        }

        // ToDisplayString() doesn't reliably come back empty for the global namespace across
        // formats - check IsGlobalNamespace directly instead of trusting the string.
        var containingNamespace = field.ContainingType.ContainingNamespace;
        var namespaceName = containingNamespace.IsGlobalNamespace ? "" : containingNamespace.ToDisplayString();

        return new FieldInfo(
            field.ContainingType.Name,
            namespaceName,
            fieldName,
            propertyName,
            underlyingType,
            styleName,
            defaultLiteral,
            errorLocation,
            GetDocs(field));
    }

    private static string? GetDocs(IFieldSymbol field)
    {
        foreach (var syntaxRef in field.DeclaringSyntaxReferences)
        {
            var fieldDecl = syntaxRef.GetSyntax().FirstAncestorOrSelf<FieldDeclarationSyntax>();

            var docTrivia = fieldDecl?.GetLeadingTrivia()
                .Select(t => t.GetStructure())
                .OfType<DocumentationCommentTriviaSyntax>()
                .FirstOrDefault();

            if (docTrivia is not null)
                return docTrivia.ToFullString().TrimEnd();
        }

        return null;
    }

    private static (string? Literal, bool Failed) FormatLiteral(TypedConstant constant, ITypeSymbol fieldType)
    {
        if (constant.IsNull)
            return (null, false);


        if (constant.Kind == TypedConstantKind.Enum)
            return ($"({constant.Type!.ToDisplayString()}){constant.Value}", false);

        if (constant.Kind != TypedConstantKind.Array && IsPrimitiveMatch(fieldType, constant.Value))
            return (FormatPrimitive(constant.Value), false);

        // the field is some other struct
        var constructed = FormatConstructed(constant, fieldType);
        return constructed is null ? (null, true) : (constructed, false);
    }

    // thanks for ts claude :p
    private static string? FormatConstructed(TypedConstant constant, ITypeSymbol fieldType)
    {
        if (fieldType is not INamedTypeSymbol named)
            return null;

        var values = constant.Kind == TypedConstantKind.Array
            ? constant.Values.Select(v => v.Value).ToArray()
            : [constant.Value];

        var ctor = named.InstanceConstructors.FirstOrDefault(c =>
            c.DeclaredAccessibility == Accessibility.Public &&
            c.Parameters.Length == values.Length &&
            c.Parameters.Zip(values, (p, v) => IsPrimitiveMatch(p.Type, v)).All(ok => ok));

        if (ctor is null)
            return null;

        var args = string.Join(", ", values.Select(FormatPrimitive));
        return $"new global::{fieldType.ToDisplayString()}({args})";
    }

    private static bool IsPrimitiveMatch(ITypeSymbol type, object? value) => (type.SpecialType, value) switch
    {
        (SpecialType.System_Boolean, bool) => true,
        (SpecialType.System_Char, char) => true,
        (SpecialType.System_String, string) => true,
        (SpecialType.System_Byte, byte) => true,
        (SpecialType.System_SByte, sbyte) => true,
        (SpecialType.System_Int16, short) => true,
        (SpecialType.System_UInt16, ushort) => true,
        (SpecialType.System_Int32, int) => true,
        (SpecialType.System_UInt32, uint) => true,
        (SpecialType.System_Int64, long) => true,
        (SpecialType.System_UInt64, ulong) => true,
        (SpecialType.System_Single, float) => true,
        (SpecialType.System_Double, double) => true,
        _ => false,
    };

    private static string FormatPrimitive(object? value) => value switch
    {
        string s => $"\"{s}\"",
        char c => $"'{c}'",
        bool b => b ? "true" : "false",
        // float/double's own ToString() renders these as "Infinity"/"NaN", which isn't
        // valid C# on its own - needs the qualified constant instead.
        float f when float.IsPositiveInfinity(f) => "float.PositiveInfinity",
        float f when float.IsNegativeInfinity(f) => "float.NegativeInfinity",
        float f when float.IsNaN(f) => "float.NaN",
        float f => $"{f}f",
        double d when double.IsPositiveInfinity(d) => "double.PositiveInfinity",
        double d when double.IsNegativeInfinity(d) => "double.NegativeInfinity",
        double d when double.IsNaN(d) => "double.NaN",
        double d => $"{d}d",
        uint u => $"{u}u",
        long l => $"{l}L",
        ulong ul => $"{ul}UL",
        _ => value?.ToString() ?? "default",
    };

    private static string GenerateClass(string @namespace, string className, List<FieldInfo> fields)
    {
        var properties = string.Join("\n\n", fields.Select(GenerateProperty));

        // ContainingNamespace.ToDisplayString() comes back empty for the global namespace -
        // "namespace ;" isn't valid C#, so that declaration line is skipped entirely then.
        var namespaceDecl = @namespace.Length == 0 ? "" : $"namespace {@namespace};\n\n";

        return $$"""
            // <auto-generated/>
            #nullable enable

            {{namespaceDecl}}partial class {{className}}
            {
            {{properties}}
            }
            """;
    }

    private static string GenerateProperty(FieldInfo field)
    {
        var isNullable = field.DefaultLiteral is null;
        var propertyType = isNullable ? $"{field.UnderlyingType}?" : field.UnderlyingType;

        var body = isNullable
            ? $"{field.FieldName} ?? GetStyleProperty<{field.UnderlyingType}?>(\"{field.StyleName}\", null)"
            : $"{field.FieldName} ?? GetStyleProperty(\"{field.StyleName}\", {field.DefaultLiteral})";

        var doc = field.DocComment is null ? "" : field.DocComment + "\n";
        return $$"""
                {{doc}}    public {{propertyType}} {{field.PropertyName}}
                {
                    get => {{body}};
                    set => {{field.FieldName}} = value;
                }
            """;
    }

    private sealed class FieldInfo
    {
        public readonly string ClassName;
        public readonly string Namespace;
        public readonly string FieldName;
        public readonly string PropertyName;
        public readonly string UnderlyingType;
        public readonly string StyleName;
        public readonly string? DefaultLiteral;
        public readonly Location? ErrorLocation;
        public readonly string? DocComment;

        public FieldInfo(string className, string @namespace, string fieldName, string propertyName,
            string underlyingType, string styleName, string? defaultLiteral, Location? errorLocation, string? docComment)
        {
            ClassName = className;
            Namespace = @namespace;
            FieldName = fieldName;
            PropertyName = propertyName;
            UnderlyingType = underlyingType;
            StyleName = styleName;
            DefaultLiteral = defaultLiteral;
            ErrorLocation = errorLocation;
            DocComment = docComment;
        }
    }
}
