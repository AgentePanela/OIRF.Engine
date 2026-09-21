using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;

namespace Engine.Generators.Networking;

/// <summary>
/// Generates WriteNetState/ReadNetState for every partial class marked [NetworkedComponent], with the
/// members marked [NetworkedField] (ordered by name).
/// </summary>
[Generator]
public sealed class ComponentStateGenerator : IIncrementalGenerator
{
    private const string NetworkedAttributeFullName = "Engine.Shared.GameObjects.NetworkedComponentAttribute";
    private const string FieldAttributeFullName = "Engine.Shared.GameObjects.NetFieldAttribute";
    private const string ComponentFullName = "Engine.Shared.GameObjects.Component";
    private const string EntityManagerParam = "entMan";

    private static readonly DiagnosticDescriptor InvalidComponent = new(
        id: Diagnostics.NetworkedComponentInvalidID,
        title: "Invalid networked component",
        messageFormat: "The [NetworkedComponent] '{0}' {1}",
        category: "Engine.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidField = new(
        id: Diagnostics.NetworkedFieldInvalidID,
        title: "Invalid networked field",
        messageFormat: "The [NetworkedField] '{0}' {1}",
        category: "Engine.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var components = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (ctx, _) => Extract(ctx))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!);

        context.RegisterSourceOutput(components, static (spc, comp) =>
        {
            foreach (var error in comp.Errors)
            {
                var descriptor = error.IsField ? InvalidField : InvalidComponent;
                spc.ReportDiagnostic(Diagnostic.Create(descriptor, error.Location ?? Location.None, error.Subject, error.Message));
            }

            if (comp.Errors.Count > 0)
                return;

            var writeLines = new List<string>();
            var readLines = new List<string>();
            var anyBad = false;

            foreach (var member in comp.Members)
            {
                var accessPath = "this." + NetSerializableResolver.EscapeIdentifier(member.Name);
                var plan = NetSerializableResolver.Resolve(member.Type, accessPath, member.Location, spc, EntityManagerParam);
                if (plan is null)
                {
                    anyBad = true;
                    continue;
                }

                writeLines.AddRange(plan.WriteLines);
                readLines.Add($"{accessPath} = {plan.ReadExpr};");
            }

            if (anyBad)
                return;

            var hintName = comp.Namespace.Length == 0
                ? $"{comp.ClassName}.ComponentState.g.cs"
                : $"{comp.Namespace}.{comp.ClassName}.ComponentState.g.cs";

            spc.AddSource(hintName, GenerateClass(comp.Namespace, comp.ClassName, writeLines, readLines));
        });
    }

    private static ComponentData? Extract(GeneratorSyntaxContext ctx)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;

        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol classSymbol)
            return null;

        if (!NetSerializableResolver.HasAttribute(classSymbol, NetworkedAttributeFullName))
            return null;

        // a class split in several partial declarations would otherwise be generated once per declaration
        if (classSymbol.DeclaringSyntaxReferences.Length > 0 && classSymbol.DeclaringSyntaxReferences[0].GetSyntax() != classDecl)
            return null;

        var containingNamespace = classSymbol.ContainingNamespace;
        var namespaceName = containingNamespace.IsGlobalNamespace ? "" : containingNamespace.ToDisplayString();
        var fullName = namespaceName.Length == 0 ? classSymbol.Name : $"{namespaceName}.{classSymbol.Name}";
        var classLocation = classDecl.Identifier.GetLocation();

        var errors = new List<ErrorData>();

        if (!classDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
            errors.Add(new ErrorData(false, fullName, "must be a partial class so its net state can be generated", classLocation));

        if (classSymbol.ContainingType is not null)
            errors.Add(new ErrorData(false, fullName, "cannot be a nested class", classLocation));

        if (classSymbol.IsGenericType)
            errors.Add(new ErrorData(false, fullName, "cannot be a generic class", classLocation));

        if (classSymbol.BaseType?.ToDisplayString() != ComponentFullName)
            errors.Add(new ErrorData(false, fullName, "must derive directly from Component", classLocation));

        var members = new List<MemberData>();
        foreach (var member in classSymbol.GetMembers())
        {
            if (member.IsStatic || member.IsImplicitlyDeclared || !NetSerializableResolver.HasAttribute(member, FieldAttributeFullName))
                continue;

            var location = member.Locations.FirstOrDefault();

            switch (member)
            {
                case IPropertySymbol prop:
                    if (prop.IsIndexer || prop.GetMethod is null || prop.SetMethod is null || prop.SetMethod.IsInitOnly)
                    {
                        errors.Add(new ErrorData(true, $"{fullName}.{prop.Name}", "needs a getter and a (non init-only) setter", location));
                        break;
                    }

                    members.Add(new MemberData(prop.Name, prop.Type, location));
                    break;

                case IFieldSymbol field:
                    if (field.IsReadOnly || field.IsConst)
                    {
                        errors.Add(new ErrorData(true, $"{fullName}.{field.Name}", "cannot be readonly or const", location));
                        break;
                    }

                    members.Add(new MemberData(field.Name, field.Type, location));
                    break;

                default:
                    errors.Add(new ErrorData(true, $"{fullName}.{member.Name}", "must be a property or a field", location));
                    break;
            }
        }

        // same order as ComponentFactory uses for the networked hash, so both sides agree on it
        members.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        return new ComponentData(classSymbol.Name, namespaceName, members, errors);
    }

    private static string GenerateClass(string @namespace, string className, List<string> writeLines, List<string> readLines)
    {
        var writes = writeLines.Count == 0 ? "" : string.Join("\n        ", writeLines);
        var reads = readLines.Count == 0 ? "" : string.Join("\n        ", readLines);

        var namespaceDecl = @namespace.Length == 0 ? "" : $"namespace {@namespace};\n\n";

        return $$"""
            // <auto-generated/>
            #nullable enable

            {{namespaceDecl}}partial class {{className}}
            {
                public override void WriteNetState(global::Lidgren.Network.NetOutgoingMessage buffer, global::Engine.Shared.GameObjects.EntityManager {{EntityManagerParam}})
                {
                    {{writes}}
                }

                public override void ReadNetState(global::Lidgren.Network.NetIncomingMessage buffer, global::Engine.Shared.GameObjects.EntityManager {{EntityManagerParam}})
                {
                    {{reads}}
                }
            }
            """;
    }

    private sealed class MemberData : IEquatable<MemberData>
    {
        public readonly string Name;
        public readonly ITypeSymbol Type;
        public readonly Location? Location;

        public MemberData(string name, ITypeSymbol type, Location? location)
        {
            Name = name;
            Type = type;
            Location = location;
        }

        public bool Equals(MemberData? other)
            => other is not null
               && Name == other.Name
               && SymbolEqualityComparer.Default.Equals(Type, other.Type)
               && Equals(Location, other.Location);

        public override bool Equals(object? obj) => obj is MemberData other && Equals(other);

        public override int GetHashCode()
            => Name.GetHashCode() * 397 ^ SymbolEqualityComparer.Default.GetHashCode(Type);
    }

    private sealed class ErrorData : IEquatable<ErrorData>
    {
        public readonly bool IsField;
        public readonly string Subject;
        public readonly string Message;
        public readonly Location? Location;

        public ErrorData(bool isField, string subject, string message, Location? location)
        {
            IsField = isField;
            Subject = subject;
            Message = message;
            Location = location;
        }

        public bool Equals(ErrorData? other)
            => other is not null
               && IsField == other.IsField
               && Subject == other.Subject
               && Message == other.Message
               && Equals(Location, other.Location);

        public override bool Equals(object? obj) => obj is ErrorData other && Equals(other);

        public override int GetHashCode() => Subject.GetHashCode() * 397 ^ Message.GetHashCode();
    }

    /// <summary>
    /// Value-equal so the incremental pipeline can tell nothing changed.
    /// </summary>
    private sealed class ComponentData : IEquatable<ComponentData>
    {
        public readonly string ClassName;
        public readonly string Namespace;
        public readonly List<MemberData> Members;
        public readonly List<ErrorData> Errors;

        public ComponentData(string className, string @namespace, List<MemberData> members, List<ErrorData> errors)
        {
            ClassName = className;
            Namespace = @namespace;
            Members = members;
            Errors = errors;
        }

        public bool Equals(ComponentData? other)
            => other is not null
               && ClassName == other.ClassName
               && Namespace == other.Namespace
               && Members.SequenceEqual(other.Members)
               && Errors.SequenceEqual(other.Errors);

        public override bool Equals(object? obj) => obj is ComponentData other && Equals(other);

        public override int GetHashCode() => ClassName.GetHashCode() * 397 ^ Namespace.GetHashCode();
    }
}
