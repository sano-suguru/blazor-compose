using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCompose.Compiler.Analysis;

/// <summary>
/// Normalizes a definition-side expression into a symbol-free <see cref="ExpressionTemplate"/> so it can
/// be inlined at any expansion site.  Replacement decisions use Roslyn symbol identity — never textual
/// substitution — so that:
/// <list type="bullet">
/// <item>identifiers bound to composable parameters become <see cref="ParameterHoleExpressionSegment"/>;</item>
/// <item>parameter identifiers inside <c>nameof(...)</c> are preserved verbatim;</item>
/// <item>unqualified type and static-member references are fully qualified;</item>
/// <item>references to non-public members record an accessibility requirement;</item>
/// <item>local, lambda, and unrecognized identifiers plus all trivia are preserved as literal text.</item>
/// </list>
/// </summary>
internal static class ExpressionTemplateFactory
{
    public static ExpressionTemplate Create(ExpressionSyntax expression, ComposableBodyContext context)
    {
        var replacements = new List<Replacement>();
        var replacedSpans = new List<Microsoft.CodeAnalysis.Text.TextSpan>();

        foreach (var node in expression.DescendantNodesAndSelf())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (node is not SimpleNameSyntax name)
                continue;

            if (IsMemberAccessName(name) || IsInsideNameof(name))
                continue;

            // Skip nodes nested inside an already-recorded replacement to avoid overlapping splices.
            if (IsNestedInReplaced(name.Span, replacedSpans))
                continue;

            var symbol = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol;
            if (symbol is null)
                continue;

            if (name is IdentifierNameSyntax
                && context.TryGetParameterOrdinal(symbol, out var ordinal))
            {
                replacements.Add(new Replacement(
                    name.Span,
                    new ParameterHoleExpressionSegment(ordinal)));
                replacedSpans.Add(name.Span);
                continue;
            }

            if (name is IdentifierNameSyntax && symbol is INamedTypeSymbol typeSymbol)
            {
                RecordAccessRequirement(typeSymbol, context);
                replacements.Add(new Replacement(
                    name.Span,
                    new LiteralExpressionSegment(
                        typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))));
                replacedSpans.Add(name.Span);
                continue;
            }

            if (name is IdentifierNameSyntax
                && symbol is IFieldSymbol or IPropertySymbol or IMethodSymbol or IEventSymbol
                && symbol.IsStatic
                && !IsQualifiedReference(name))
            {
                RecordAccessRequirement(symbol, context);
                var containing = symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                replacements.Add(new Replacement(
                    name.Span,
                    new LiteralExpressionSegment($"{containing}.{symbol.Name}")));
                replacedSpans.Add(name.Span);
            }
        }

        return replacements.Count == 0
            ? ExpressionTemplate.Literal(expression.ToString())
            : Splice(expression, replacements);
    }

    private static ExpressionTemplate Splice(ExpressionSyntax expression, List<Replacement> replacements)
    {
        replacements.Sort(static (left, right) => left.Span.Start.CompareTo(right.Span.Start));

        var baseText = expression.ToString();
        var baseStart = expression.Span.Start;

        var segments = ImmutableArray.CreateBuilder<ExpressionSegment>();
        var cursor = 0;

        foreach (var replacement in replacements)
        {
            var relativeStart = replacement.Span.Start - baseStart;
            if (relativeStart > cursor)
                segments.Add(new LiteralExpressionSegment(baseText.Substring(cursor, relativeStart - cursor)));

            segments.Add(replacement.Segment);
            cursor = relativeStart + replacement.Span.Length;
        }

        if (cursor < baseText.Length)
            segments.Add(new LiteralExpressionSegment(baseText.Substring(cursor)));

        return ExpressionTemplate.Create(segments.ToImmutable());
    }

    private static void RecordAccessRequirement(ISymbol symbol, ComposableBodyContext context)
    {
        var kind = symbol.DeclaredAccessibility switch
        {
            Accessibility.Private => (ComposableAccessRequirementKind?)ComposableAccessRequirementKind.SameContainingType,
            Accessibility.Protected => ComposableAccessRequirementKind.DerivedContainingType,
            Accessibility.ProtectedOrInternal => ComposableAccessRequirementKind.DerivedContainingType,
            Accessibility.ProtectedAndInternal => ComposableAccessRequirementKind.DerivedContainingType,
            _ => null,
        };

        if (kind is null)
            return;

        context.AddAccessRequirement(new ComposableAccessRequirement(
            kind.Value,
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
    }

    private static bool IsMemberAccessName(SimpleNameSyntax name) =>
        name.Parent switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name == name,
            QualifiedNameSyntax qualified => qualified.Right == name,
            MemberBindingExpressionSyntax binding => binding.Name == name,
            _ => false,
        };

    private static bool IsQualifiedReference(SimpleNameSyntax name) =>
        name.Parent is MemberAccessExpressionSyntax or QualifiedNameSyntax or MemberBindingExpressionSyntax;

    private static bool IsInsideNameof(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
                    ArgumentList: var arguments,
                }
                && arguments.Span.Contains(node.Span))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNestedInReplaced(
        Microsoft.CodeAnalysis.Text.TextSpan span,
        List<Microsoft.CodeAnalysis.Text.TextSpan> replacedSpans)
    {
        foreach (var replaced in replacedSpans)
        {
            if (replaced.Contains(span))
                return true;
        }

        return false;
    }

    private readonly record struct Replacement(
        Microsoft.CodeAnalysis.Text.TextSpan Span,
        ExpressionSegment Segment);
}
