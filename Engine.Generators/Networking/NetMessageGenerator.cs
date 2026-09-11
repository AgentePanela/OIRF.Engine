using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;

namespace Engine.Generators.Networking;

/// <summary>
/// Generates WriteToBuffer/ReadFromBuffer for every partial class deriving
/// from NetMessage.
/// </summary>
[Generator]
public sealed class NetMessageGenerator : IIncrementalGenerator
{
    private const string BaseClassFullName = "Engine.Shared.Networking.NetMessage";

    private static readonly DiagnosticDescriptor UnpartialClass = new(
        id: Diagnostics.MessageNotPartialID,
        title: "Class is not partial",
        messageFormat: "The NetMessage: '{0}' should be partial class",
        category: "Engine.Generators",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var messages = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 },
                transform: static (ctx, _) => Extract(ctx))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        context.RegisterSourceOutput(messages, static (spc, msg) =>
        {
            if (!msg.IsPartial)
            {
                spc.ReportDiagnostic(Diagnostic.Create(UnpartialClass, msg.ClassLocation ?? Location.None, msg.FullName));
                return;
            }

            var writeLines = new List<string>();
            var readLines = new List<string>();
            var anyBad = false;

            foreach (var prop in msg.Properties)
            {
                var escapedName = NetSerializableResolver.EscapeIdentifier(prop.Name);
                var plan = NetSerializableResolver.Resolve(prop.Type, escapedName, prop.Location, spc);
                if (plan is null)
                {
                    anyBad = true;
                    continue;
                }

                writeLines.AddRange(plan.WriteLines);
                readLines.Add($"{escapedName} = {plan.ReadExpr};");
            }

            if (anyBad)
                return;

            var hintName = msg.Namespace.Length == 0
                ? $"{msg.ClassName}.NetMessage.g.cs"
                : $"{msg.Namespace}.{msg.ClassName}.NetMessage.g.cs";

            spc.AddSource(hintName, GenerateClass(msg.Namespace, msg.ClassName, writeLines, readLines, msg.NeedsParameterlessCtor));
        });
    }

    private static MessageData? Extract(GeneratorSyntaxContext ctx)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;

        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol classSymbol)
            return null;

        if (!InheritsNetMessage(classSymbol))
            return null;

        var fullNameForWarning = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? classSymbol.Name
            : $"{classSymbol.ContainingNamespace.ToDisplayString()}.{classSymbol.Name}";

        if (!classDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
            return MessageData.NotPartial(fullNameForWarning, classDecl.Identifier.GetLocation());

        var properties = new List<PropertyData>();
        foreach (var member in classSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            // No setter - nothing for ReadFromBuffer to assign into.
            if (member.IsStatic || member.SetMethod is null || member.IsOverride)
                continue;

            if (NetSerializableResolver.HasAttribute(member, NetSerializableResolver.IgnoreAttributeFullName))
                continue;

            properties.Add(new PropertyData(member.Name, member.Type, member.Locations.FirstOrDefault()));
        }

        var containingNamespace = classSymbol.ContainingNamespace;
        var namespaceName = containingNamespace.IsGlobalNamespace ? "" : containingNamespace.ToDisplayString();
        var fullName = namespaceName.Length == 0 ? classSymbol.Name : $"{namespaceName}.{classSymbol.Name}";
        var needsParameterlessCtor = !classSymbol.InstanceConstructors.Any(c =>
            c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

        return new MessageData(classSymbol.Name, namespaceName, fullName, properties, needsParameterlessCtor);
    }

    private static bool InheritsNetMessage(INamedTypeSymbol classSymbol)
    {
        for (var baseType = classSymbol.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.ToDisplayString() == BaseClassFullName)
                return true;
        }

        return false;
    }

    private static string GenerateClass(string @namespace, string className, List<string> writeLines, List<string> readLines, bool needsParameterlessCtor)
    {
        var writes = writeLines.Count == 0 ? "" : string.Join("\n        ", writeLines);
        var reads = readLines.Count == 0 ? "" : string.Join("\n        ", readLines);

        var namespaceDecl = @namespace.Length == 0 ? "" : $"namespace {@namespace};\n\n";
        var ctor = needsParameterlessCtor ? $"public {className}() {{ }}\n\n    " : "";

        return $$"""
            // <auto-generated/>
            #nullable enable

            {{namespaceDecl}}partial class {{className}}
            {
                {{ctor}}public override void WriteToBuffer(global::Lidgren.Network.NetOutgoingMessage buffer)
                {
                    {{writes}}
                }

                public override void ReadFromBuffer(global::Lidgren.Network.NetIncomingMessage buffer)
                {
                    {{reads}}
                }
            }
            """;
    }

    private sealed class PropertyData : IEquatable<PropertyData>
    {
        public readonly string Name;
        public readonly ITypeSymbol Type;
        public readonly Location? Location;

        public PropertyData(string name, ITypeSymbol type, Location? location)
        {
            Name = name;
            Type = type;
            Location = location;
        }

        public bool Equals(PropertyData? other)
            => other is not null
               && Name == other.Name
               && SymbolEqualityComparer.Default.Equals(Type, other.Type)
               && Equals(Location, other.Location);

        public override bool Equals(object? obj) => obj is PropertyData other && Equals(other);

        public override int GetHashCode()
            => Name.GetHashCode() * 397 ^ SymbolEqualityComparer.Default.GetHashCode(Type);
    }

    /// <summary>
    /// Value-equal for the same reason as PropertyData - see there.
    /// </summary>
    private sealed class MessageData : IEquatable<MessageData>
    {
        public readonly string ClassName;
        public readonly string Namespace;
        public readonly string FullName;
        public readonly List<PropertyData> Properties;
        public readonly bool NeedsParameterlessCtor;
        public readonly bool IsPartial;
        public readonly Location? ClassLocation;

        public MessageData(string className, string @namespace, string fullName, List<PropertyData> properties, bool needsParameterlessCtor)
        {
            ClassName = className;
            Namespace = @namespace;
            FullName = fullName;
            Properties = properties;
            NeedsParameterlessCtor = needsParameterlessCtor;
            IsPartial = true;
        }

        private MessageData(string fullName, Location? classLocation)
        {
            ClassName = fullName;
            Namespace = "";
            FullName = fullName;
            Properties = new List<PropertyData>();
            IsPartial = false;
            ClassLocation = classLocation;
        }

        public static MessageData NotPartial(string fullName, Location? classLocation)
            => new(fullName, classLocation);

        public bool Equals(MessageData? other)
            => other is not null
               && ClassName == other.ClassName
               && Namespace == other.Namespace
               && FullName == other.FullName
               && NeedsParameterlessCtor == other.NeedsParameterlessCtor
               && IsPartial == other.IsPartial
               && Equals(ClassLocation, other.ClassLocation)
               && Properties.SequenceEqual(other.Properties);

        public override bool Equals(object? obj) => obj is MessageData other && Equals(other);

        public override int GetHashCode() => FullName.GetHashCode();
    }
}
