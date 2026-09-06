using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Engine.Generators.Networking;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NetMessageMethodsCodeFixProvider)), Shared]
public sealed class NetMessageMethodsCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create(Diagnostics.NetFieldUnsupportedTypeID);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics.First();
        var classDecl = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (classDecl is null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add WriteToBuffer/ReadFromBuffer stubs",
                createChangedDocument: ct => AddMethodsAsync(context.Document, classDecl, ct),
                equivalenceKey: nameof(NetMessageMethodsCodeFixProvider)),
            diagnostic);
    }

    private static async Task<Document> AddMethodsAsync(Document document, ClassDeclarationSyntax classDecl, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
            return document;

        var hasWrite = classDecl.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.Text == "WriteToBuffer");
        var hasRead = classDecl.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.Text == "ReadFromBuffer");

        var newMembers = new List<MemberDeclarationSyntax>();

        if (!hasWrite)
        {
            newMembers.Add(SyntaxFactory.ParseMemberDeclaration(
                "public void WriteToBuffer(Lidgren.Network.NetOutgoingMessage buffer)\n" +
                "{\n" +
                "    throw new System.NotImplementedException();\n" +
                "}\n")!);
        }

        if (!hasRead)
        {
            newMembers.Add(SyntaxFactory.ParseMemberDeclaration(
                "public void ReadFromBuffer(Lidgren.Network.NetIncomingMessage buffer)\n" +
                "{\n" +
                "    throw new System.NotImplementedException();\n" +
                "}\n")!);
        }

        if (newMembers.Count == 0)
            return document;

        var newClassDecl = classDecl.AddMembers(newMembers.ToArray());
        var newRoot = root.ReplaceNode(classDecl, newClassDecl);

        return document.WithSyntaxRoot(newRoot);
    }
}
