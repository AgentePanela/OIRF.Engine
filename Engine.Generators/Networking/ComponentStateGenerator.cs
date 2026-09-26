using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Text;

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
    private const string AutoDirtyAttributeFullName = "Engine.Shared.GameObjects.AutoDirtyAttribute";
    private const string ComponentFullName = "Engine.Shared.GameObjects.Component";
    private const string GameTickFullName = "global::Engine.Shared.Timing.GameTick";
    private const string EntityManagerParam = "entMan";
    private static readonly string[] ManualStateMethodNames = { "GetNetState", "HandleNetState" };

    // the generated file has no usings, so every type it names has to be fully qualified
    private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private static readonly string[] CollectionTypes =
    {
        "System.Collections.Generic.List<T>",
        "System.Collections.Generic.HashSet<T>",
        "System.Collections.Generic.Dictionary<TKey, TValue>",
    };

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

    private static readonly DiagnosticDescriptor AutoDirtyCollection = new(
        id: Diagnostics.AutoDirtyCollectionID,
        title: "[AutoDirty] on a collection",
        messageFormat: "'{0}' is a collection, so [AutoDirty] only catches replacing it whole - adding, removing or changing an item runs no setter and still needs EntityManager.Dirty",
        category: "Engine.Generators",
        DiagnosticSeverity.Warning,
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

            // the component replicates itself by hand via GetNetState/HandleNetState - nothing to generate
            if (comp.IsManual)
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

            foreach (var member in comp.Members)
            {
                if (member.AutoDirty && IsCollection(member.Type))
                    spc.ReportDiagnostic(Diagnostic.Create(AutoDirtyCollection, member.Location ?? Location.None, member.Name));
            }

            spc.AddSource(hintName, GenerateClass(comp.Namespace, comp.ClassName, comp.Members, writeLines, readLines));
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

        // GetNetState/HandleNetState overridden - this component replicates itself by handd
        var isManual = classSymbol.GetMembers().OfType<IMethodSymbol>()
            .Any(m => m.IsOverride && ManualStateMethodNames.Contains(m.Name));

        var errors = new List<ErrorData>();

        // these only matter for code generation - a manual component (GetNetState/HandleNetState) generates
        // nothing, so it doesn't need to be partial, top-level, non-generic or a direct Component subclass
        if (!isManual)
        {
            if (!classDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
                errors.Add(new ErrorData(false, fullName, "must be a partial class so its net state can be generated", classLocation));

            if (classSymbol.ContainingType is not null)
                errors.Add(new ErrorData(false, fullName, "cannot be a nested class", classLocation));

            if (classSymbol.IsGenericType)
                errors.Add(new ErrorData(false, fullName, "cannot be a generic class", classLocation));

            if (classSymbol.BaseType?.ToDisplayString() != ComponentFullName)
                errors.Add(new ErrorData(false, fullName, "must derive directly from Component", classLocation));
        }

        var classAutoDirty = NetSerializableResolver.HasAttribute(classSymbol, AutoDirtyAttributeFullName);

        var members = new List<MemberData>();
        foreach (var member in classSymbol.GetMembers())
        {
            if (member.IsStatic || member.IsImplicitlyDeclared || !NetSerializableResolver.HasAttribute(member, FieldAttributeFullName))
                continue;

            var location = member.Locations.FirstOrDefault();
            var autoDirty = classAutoDirty || NetSerializableResolver.HasAttribute(member, AutoDirtyAttributeFullName);

            switch (member)
            {
                case IPropertySymbol prop:
                    if (prop.IsIndexer || prop.GetMethod is null || prop.SetMethod is null || prop.SetMethod.IsInitOnly)
                    {
                        errors.Add(new ErrorData(true, $"{fullName}.{prop.Name}", "needs a getter and a (non init-only) setter", location));
                        break;
                    }

                    // the generator writes the body, so it has to be the other half of a partial property - and a
                    // partial property we don't implement would not compile at all
                    if (autoDirty && !prop.IsPartialDefinition)
                    {
                        errors.Add(new ErrorData(true, $"{fullName}.{prop.Name}", "is [AutoDirty], so it has to be declared 'partial' - the generator writes the setter that stamps the tick", location));
                        break;
                    }

                    if (!autoDirty && prop.IsPartialDefinition)
                    {
                        errors.Add(new ErrorData(true, $"{fullName}.{prop.Name}", "is partial but not [AutoDirty], so nothing implements it - add [AutoDirty] or drop the partial", location));
                        break;
                    }

                    members.Add(new MemberData(prop.Name, prop.Type, location, autoDirty, AccessibilityOf(prop.DeclaredAccessibility)));
                    break;

                case IFieldSymbol field:
                    if (field.IsReadOnly || field.IsConst)
                    {
                        errors.Add(new ErrorData(true, $"{fullName}.{field.Name}", "cannot be readonly or const", location));
                        break;
                    }

                    if (autoDirty)
                    {
                        errors.Add(new ErrorData(true, $"{fullName}.{field.Name}", "cannot be [AutoDirty] as a field - there is no setter to own, make it a partial property", location));
                        break;
                    }

                    members.Add(new MemberData(field.Name, field.Type, location));
                    break;

                default:
                    errors.Add(new ErrorData(true, $"{fullName}.{member.Name}", "must be a property or a field", location));
                    break;
            }
        }

        if (isManual && members.Count > 0)
            errors.Add(new ErrorData(false, fullName, "implements GetNetState/HandleNetState manually AND has [NetField] members - pick one, the fields would be silently ignored", classLocation));

        // same order as ComponentFactory uses for the networked hash, so both sides agree on it
        members.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        return new ComponentData(classSymbol.Name, namespaceName, members, errors, isManual);
    }

    private static string GenerateClass(string @namespace, string className, List<MemberData> members, List<string> writeLines, List<string> readLines)
    {
        var writes = writeLines.Count == 0 ? "" : string.Join("\n        ", writeLines);
        var reads = readLines.Count == 0 ? "" : string.Join("\n        ", readLines);

        var namespaceDecl = @namespace.Length == 0 ? "" : $"namespace {@namespace};\n\n";

        return $$"""
            // <auto-generated/>
            #nullable enable

            {{namespaceDecl}}partial class {{className}}
            {
                {{GenerateFieldTicks(members)}}public override void WriteNetState(global::Lidgren.Network.NetBuffer buffer, global::Engine.Shared.GameObjects.EntityManager {{EntityManagerParam}})
                {
                    {{writes}}
                }

                public override void ReadNetState(global::Lidgren.Network.NetBuffer buffer, global::Engine.Shared.GameObjects.EntityManager {{EntityManagerParam}})
                {
                    {{reads}}
                }
            }
            """;
    }

    /// <summary>
    /// The per field ticks a delta is built from, plus the setters of the [AutoDirty] members.
    /// </summary>
    private static string GenerateFieldTicks(List<MemberData> members)
    {
        if (members.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.Append("public override int NetFieldCount => ").Append(members.Count).Append(";\n\n    ");

        if (!members.Any(static m => m.AutoDirty))
            return sb.ToString();

        foreach (var member in members)
            sb.Append("private ").Append(GameTickFullName).Append(" __netTick_").Append(member.Name).Append(";\n    ");

        sb.Append("\n    public override ").Append(GameTickFullName).Append(" GetFieldTick(int index) => index switch\n    {\n");
        for (var i = 0; i < members.Count; i++)
            sb.Append("        ").Append(i).Append(" => __netTick_").Append(members[i].Name).Append(",\n");
        sb.Append("        _ => LastModifiedTick,\n    };\n\n    ");

        sb.Append("public override void SetFieldTick(int index, ").Append(GameTickFullName).Append(" tick)\n    {\n        switch (index)\n        {\n");
        for (var i = 0; i < members.Count; i++)
            sb.Append("            case ").Append(i).Append(": __netTick_").Append(members[i].Name).Append(" = tick; break;\n");
        sb.Append("        }\n    }\n\n    ");

        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (!member.AutoDirty)
                continue;

            sb.Append(member.Accessibility).Append(" partial ").Append(member.Type.ToDisplayString(FullyQualified))
                .Append(' ').Append(NetSerializableResolver.EscapeIdentifier(member.Name)).Append("\n    {\n")
                .Append("        get => field;\n")
                .Append("        set { field = value; DirtyField(").Append(i).Append("); }\n")
                .Append("    }\n\n    ");
        }

        return sb.ToString();
    }

    private static string AccessibilityOf(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Private => "private",
        Accessibility.Protected => "protected",
        Accessibility.Internal => "internal",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => "public",
    };

    private static bool IsCollection(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol)
            return true;

        var definition = (type as INamedTypeSymbol)?.OriginalDefinition.ToDisplayString();
        return definition is not null && CollectionTypes.Contains(definition);
    }

    private sealed class MemberData : IEquatable<MemberData>
    {
        public readonly string Name;
        public readonly ITypeSymbol Type;
        public readonly Location? Location;

        /// <summary>
        /// The generator owns this member's setter
        /// </summary>
        public readonly bool AutoDirty;

        /// <summary>
        /// Accessibility to repeat on the implementing declaration.
        /// </summary>
        public readonly string Accessibility;

        public MemberData(string name, ITypeSymbol type, Location? location, bool autoDirty = false, string accessibility = "public")
        {
            Name = name;
            Type = type;
            Location = location;
            AutoDirty = autoDirty;
            Accessibility = accessibility;
        }

        public bool Equals(MemberData? other)
            => other is not null
               && Name == other.Name
               && AutoDirty == other.AutoDirty
               && Accessibility == other.Accessibility
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
        public readonly bool IsManual;

        public ComponentData(string className, string @namespace, List<MemberData> members, List<ErrorData> errors, bool isManual)
        {
            ClassName = className;
            Namespace = @namespace;
            Members = members;
            Errors = errors;
            IsManual = isManual;
        }

        public bool Equals(ComponentData? other)
            => other is not null
               && ClassName == other.ClassName
               && Namespace == other.Namespace
               && IsManual == other.IsManual
               && Members.SequenceEqual(other.Members)
               && Errors.SequenceEqual(other.Errors);

        public override bool Equals(object? obj) => obj is ComponentData other && Equals(other);

        public override int GetHashCode() => ClassName.GetHashCode() * 397 ^ Namespace.GetHashCode();
    }
}
