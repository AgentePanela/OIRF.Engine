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

/// <summary>
/// Quick-fix for EA002 (unsupported type on a networked message property):
/// stubs out WriteToBuffer/ReadFromBuffer on the class so theres somewhere
/// to write them by hand instead.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NetMessageMethodsCodeFixProvider)), Shared]
public sealed class NetMessageMethodsCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create(Diagnostics.NetFieldUnsupportedTypeID);

    public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics.First();
        var classDecl = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent?
            .AncestorsAndSelf().OfType<ClassDeclarationSyntax>().FirstOrDefault();

        if (classDecl is null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Add WriteToBuffer/ReadFromBuffer",
                createChangedDocument: ct => AddStubMethodsAsync(context.Document, root, classDecl, ct),
                equivalenceKey: nameof(NetMessageMethodsCodeFixProvider)),
            diagnostic);
    }

    private static Task<Document> AddStubMethodsAsync(Document document, SyntaxNode root, ClassDeclarationSyntax classDecl, CancellationToken ct)
    {
        var hasWrite = classDecl.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.Text == "WriteToBuffer");
        var hasRead = classDecl.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.Text == "ReadFromBuffer");

        var newMembers = classDecl.Members;

        if (!hasWrite)
            newMembers = newMembers.Add(ParseMethod("public override void WriteToBuffer(global::Lidgren.Network.NetOutgoingMessage buffer) => throw new global::System.NotImplementedException();"));

        if (!hasRead)
            newMembers = newMembers.Add(ParseMethod("public override void ReadFromBuffer(global::Lidgren.Network.NetIncomingMessage buffer) => throw new global::System.NotImplementedException();"));

        var newClassDecl = classDecl.WithMembers(newMembers);
        var newRoot = root.ReplaceNode(classDecl, newClassDecl);

        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }

    private static MethodDeclarationSyntax ParseMethod(string code)
        => (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(code)!;
}
