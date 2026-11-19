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

namespace Moirai.EventBus.Roslyn.CodeFixes;

/// <summary>
/// MIRAI701 修复：为 OnXxxEvent 回调的事件参数补上 in 修饰（ref/out 同步替换为 in）。
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddInParameterCodeFixProvider))]
[Shared]
public sealed class AddInParameterCodeFixProvider : CodeFixProvider
{
    private const string Title = "为事件参数添加 in 修饰（OnXxxEvent(in XxxEvent evt)）";

    public override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create(Diagnostics.InParameterRequiredId);

    public sealed override FixAllProvider? GetFixAllProvider()
        => WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (diagnostic.Id != Diagnostics.InParameterRequiredId) continue;
            if (root.FindNode(diagnostic.Location.SourceSpan) is not ParameterSyntax parameter) continue;
            if (parameter.Type is null) continue;
            if (parameter.Modifiers.Any(SyntaxKind.InKeyword)) continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    cancellationToken => AddInModifierAsync(context.Document, parameter, cancellationToken),
                    equivalenceKey: Title),
                diagnostic);
        }
    }

    private static async Task<Document> AddInModifierAsync(
        Document document,
        ParameterSyntax parameter,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;

        ParameterSyntax newParameter = BuildParameter(parameter);
        var newRoot = root.ReplaceNode(parameter, newParameter);
        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>构造带 in 修饰的参数：ref/out 原位替换为 in；无修饰符时插入 in（保留原参数一切 trivia）。</summary>
    private static ParameterSyntax BuildParameter(ParameterSyntax parameter)
    {
        var inToken = SyntaxFactory.Token(SyntaxKind.InKeyword);

        var refOrOut = parameter.Modifiers.FirstOrDefault(m =>
            m.IsKind(SyntaxKind.RefKeyword) || m.IsKind(SyntaxKind.OutKeyword));

        if (refOrOut != default)
        {
            // 原位替换 ref/out → in，保留两侧 trivia
            var replaced = inToken
                .WithLeadingTrivia(refOrOut.LeadingTrivia)
                .WithTrailingTrivia(refOrOut.TrailingTrivia);
            var newModifiers = parameter.Modifiers.Replace(refOrOut, replaced);
            return parameter.WithModifiers(newModifiers);
        }

        // 插入 in 于参数最前：接管首个修饰符/类型的 leading trivia
        SyntaxToken inserted = inToken;
        if (parameter.Modifiers.Count > 0)
        {
            var first = parameter.Modifiers[0];
            inserted = inserted.WithLeadingTrivia(first.LeadingTrivia);
            var replacedFirst = first.WithLeadingTrivia(default(SyntaxTriviaList));
            var rest = parameter.Modifiers.Replace(first, replacedFirst);
            return parameter.WithModifiers(SyntaxFactory.TokenList(rest.Insert(0, inserted)));
        }

        if (parameter.Type is not null)
        {
            var firstTypeToken = parameter.Type.GetFirstToken();
            inserted = inserted
                .WithLeadingTrivia(firstTypeToken.LeadingTrivia)
                .WithTrailingTrivia(SyntaxFactory.Space);
            var newType = parameter.Type.ReplaceToken(firstTypeToken, firstTypeToken.WithLeadingTrivia(default(SyntaxTriviaList)));
            return parameter.WithType(newType).WithModifiers(SyntaxFactory.TokenList(inserted));
        }

        return parameter.WithModifiers(SyntaxFactory.TokenList(inserted));
    }
}
