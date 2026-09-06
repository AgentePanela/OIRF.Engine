using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;

namespace Engine.Generators;

[Generator]
public sealed class NetMessageGenerator : IIncrementalGenerator
{
    private const string InterfaceFullName = "Engine.Shared.Networking.INetMessage";
    private const string IgnoreAttributeFullName = "Engine.Shared.Networking.NetIgnoreAttribute";

    private static readonly DiagnosticDescriptor UnsupportedType = new(
        id: Diagnostics.NetFieldUnsupportedTypeID,
        title: "Unsupported INetMessage property type",
        messageFormat: "Type {0} in {1} isn't serializable - Try adding [NetSerializable] or write WriteToBuffer/ReadFromBuffer by hand instead",
        category: "Engine.Generators",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

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

            foreach (var bad in msg.Fields.Where(f => f.ReadMethod is null))
                spc.ReportDiagnostic(Diagnostic.Create(UnsupportedType, bad.Location ?? Location.None, bad.TypeName, bad.PropertyName));

            var ok = msg.Fields.Where(f => f.ReadMethod is not null).ToList();

            var hintName = msg.Namespace.Length == 0
                ? $"{msg.ClassName}.NetMessage.g.cs"
                : $"{msg.Namespace}.{msg.ClassName}.NetMessage.g.cs";

            spc.AddSource(hintName, GenerateClass(msg.Namespace, msg.ClassName, ok, msg.NeedsParameterlessCtor));
        });
    }

    private static MessageData? Extract(GeneratorSyntaxContext ctx)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;

        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol classSymbol)
            return null;

        if (!classSymbol.AllInterfaces.Any(i => i.ToDisplayString() == InterfaceFullName))
            return null;

        var fullNameForWarning = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? classSymbol.Name
            : $"{classSymbol.ContainingNamespace.ToDisplayString()}.{classSymbol.Name}";

        if (!classDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
            return MessageData.NotPartial(fullNameForWarning, classDecl.Identifier.GetLocation());

        var fields = new List<FieldData>();
        foreach (var member in classSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            // No setter. nothing for ReadFromBuffer to assign into
            if (member.IsStatic || member.SetMethod is null)
                continue;

            if (member.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == IgnoreAttributeFullName))
                continue;

            var typeName = member.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();
            fields.Add(new FieldData(member.Name, typeName, GetReadMethod(typeName), member.Locations.FirstOrDefault()));
        }

        var containingNamespace = classSymbol.ContainingNamespace;
        var namespaceName = containingNamespace.IsGlobalNamespace ? "" : containingNamespace.ToDisplayString();
        var fullName = namespaceName.Length == 0 ? classSymbol.Name : $"{namespaceName}.{classSymbol.Name}";
        var needsParameterlessCtor = !classSymbol.InstanceConstructors.Any(c =>
            c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

        return new MessageData(classSymbol.Name, namespaceName, fullName, fields, needsParameterlessCtor);
    }

    // Every supported type shares the same Write() - only Read differs per type.
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

    private static string GenerateClass(string @namespace, string className, List<FieldData> fields, bool needsParameterlessCtor)
    {
        var writes = fields.Count == 0 ? "" : string.Join("\n            ", fields.Select(f => $"buffer.Write({f.PropertyName});"));
        var reads = fields.Count == 0 ? "" : string.Join("\n            ", fields.Select(f => $"{f.PropertyName} = buffer.{f.ReadMethod}();"));

        var namespaceDecl = @namespace.Length == 0 ? "" : $"namespace {@namespace};\n\n";
        var ctor = needsParameterlessCtor ? $"public {className}() {{ }}\n\n    " : "";

        return $$"""
            // <auto-generated/>
            #nullable enable

            {{namespaceDecl}}partial class {{className}}
            {
                {{ctor}}public void WriteToBuffer(global::Lidgren.Network.NetOutgoingMessage buffer)
                {
                    {{writes}}
                }

                public void ReadFromBuffer(global::Lidgren.Network.NetIncomingMessage buffer)
                {
                    {{reads}}
                }
            }
            """;
    }

    private sealed class FieldData
    {
        public readonly string PropertyName;
        public readonly string TypeName;
        public readonly string? ReadMethod;
        public readonly Location? Location;

        public FieldData(string propertyName, string typeName, string? readMethod, Location? location)
        {
            PropertyName = propertyName;
            TypeName = typeName;
            ReadMethod = readMethod;
            Location = location;
        }
    }

    private sealed class MessageData
    {
        public readonly string ClassName;
        public readonly string Namespace;
        public readonly string FullName;
        public readonly List<FieldData> Fields;
        public readonly bool NeedsParameterlessCtor;
        public readonly bool IsPartial;
        public readonly Location? ClassLocation;

        public MessageData(string className, string @namespace, string fullName, List<FieldData> fields, bool needsParameterlessCtor)
        {
            ClassName = className;
            Namespace = @namespace;
            FullName = fullName;
            Fields = fields;
            NeedsParameterlessCtor = needsParameterlessCtor;
            IsPartial = true;
        }

        private MessageData(string fullName, Location? classLocation)
        {
            ClassName = fullName;
            Namespace = "";
            FullName = fullName;
            Fields = new List<FieldData>();
            IsPartial = false;
            ClassLocation = classLocation;
        }

        public static MessageData NotPartial(string fullName, Location? classLocation)
            => new(fullName, classLocation);
    }
}
