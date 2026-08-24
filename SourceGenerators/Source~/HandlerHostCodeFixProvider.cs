using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// 为 MIRAI001 诊断提供快速修复：生成空的 CreateDefaultHandler 方法。
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HandlerHostCodeFixProvider))]
    public class HandlerHostCodeFixProvider : CodeFixProvider
    {
        private const string Title = "生成 CreateDefaultHandler()";

        public sealed override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create("MIRAI001");

        public sealed override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            var classDecl = root!.FindNode(diagnosticSpan) as ClassDeclarationSyntax;
            if (classDecl == null)
                return;

            var handlerTypeName = diagnostic.Properties.TryGetValue("HandlerTypeName", out var name) && name != null
                ? name
                : "FrameworkHandler";

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Title,
                    createChangedDocument: c => GenerateMethodAsync(context.Document, classDecl, handlerTypeName, c),
                    equivalenceKey: Title),
                diagnostic);
        }

        private static async Task<Document> GenerateMethodAsync(
            Document document,
            ClassDeclarationSyntax classDecl,
            string handlerTypeName,
            CancellationToken cancellationToken)
        {
            var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);

            var method = SyntaxFactory.MethodDeclaration(
                SyntaxFactory.ParseTypeName(handlerTypeName),
                SyntaxFactory.Identifier(Constants.CreateDefaultHandlerMethodName))
                .WithModifiers(SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PrivateKeyword),
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                .WithExpressionBody(
                    SyntaxFactory.ArrowExpressionClause(
                        SyntaxFactory.ThrowExpression(
                            SyntaxFactory.ObjectCreationExpression(
                                SyntaxFactory.ParseTypeName("System.NotImplementedException"))
                                .WithArgumentList(SyntaxFactory.ArgumentList()))))
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                .WithLeadingTrivia(
                    SyntaxFactory.Comment("/// <summary>"),
                    SyntaxFactory.Comment("/// 创建默认处理器。"),
                    SyntaxFactory.Comment("/// </summary>"),
                    SyntaxFactory.Comment("/// <returns>默认处理器实例。</returns>"),
                    SyntaxFactory.CarriageReturnLineFeed);

            editor.AddMember(classDecl, method);
            return editor.GetChangedDocument();
        }
    }
}
