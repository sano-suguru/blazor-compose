using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using BlazorCompose.Compiler.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCompose.Compiler;

/// <summary>Creates a <see cref="ComponentModel"/> from a syntax context for a candidate class node.</summary>
internal static class ComponentModelFactory
{
    /// <summary>
    /// Returns a <see cref="ComponentModel"/> when <paramref name="syntaxContext"/> represents a partial
    /// class that directly or indirectly inherits from <c>BlazorCompose.ComposeComponentBase</c> and
    /// whose <c>Body</c> expression is fully SSC-analyzable, or <see langword="null"/> otherwise.
    /// </summary>
    internal static ComponentModel? TryCreate(
        GeneratorSyntaxContext syntaxContext,
        KnownSymbols? knownSymbols,
        CancellationToken cancellationToken)
    {
        var classDeclaration = (ClassDeclarationSyntax)syntaxContext.Node;

        if (!classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            return null;

        var symbol = syntaxContext.SemanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken);
        if (symbol is null)
            return null;

        // Nested classes require wrapping the generated code inside the outer class hierarchy;
        // that complexity is out of scope for this task.  Skip them so the generator does not
        // emit a structurally incorrect top-level partial class.
        if (symbol.ContainingType is not null)
            return null;

        if (!ComposeComponentBaseFacts.InheritsFromComposeComponentBase(symbol))
            return null;

        // Without known symbols the Body cannot be analyzed; skip rather than emit empty source.
        if (knownSymbols is null)
            return null;

        var namespaceName = symbol.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString()
            : null;

        // Include namespace in the hint name to prevent collisions when two components share
        // the same simple class name across different namespaces.
        var hintName = namespaceName is not null
            ? $"{namespaceName}.{symbol.MetadataName}.g.cs"
            : $"{symbol.MetadataName}.g.cs";

        var rootNode = TryExtractBodyNode(classDeclaration, syntaxContext.SemanticModel, knownSymbols, cancellationToken);

        // An unrecognized or unsupported Body shape must not produce an empty RenderBody; returning
        // null here causes CS0534 in the user's compilation, which is the correct failure signal
        // until the Opaque/BC2001 path is implemented.
        if (rootNode is null)
            return null;

        return new ComponentModel(
            HintName: hintName,
            ClassName: symbol.Name,
            Namespace: namespaceName,
            RootNode: rootNode);
    }

    // ---------------------------------------------------------------------------
    // Body extraction
    // ---------------------------------------------------------------------------

    private static RenderNode? TryExtractBodyNode(
        ClassDeclarationSyntax classDecl,
        SemanticModel semanticModel,
        KnownSymbols symbols,
        CancellationToken ct)
    {
        foreach (var member in classDecl.Members)
        {
            if (member is not PropertyDeclarationSyntax prop)
                continue;

            if (prop.Identifier.Text != "Body")
                continue;

            if (!prop.Modifiers.Any(SyntaxKind.OverrideKeyword))
                continue;

            // Support expression-bodied properties only: `protected override View Body => expr;`
            if (prop.ExpressionBody is { Expression: var bodyExpr })
                return TryAnalyzeExpression(bodyExpr, semanticModel, symbols, ct);
        }

        return null;
    }

    /// <summary>
    /// Recursively classifies <paramref name="expr"/> as SSC and returns the corresponding
    /// <see cref="RenderNode"/>, or <see langword="null"/> when the expression is opaque or
    /// unrecognized.  Dynamic argument text is captured verbatim from the syntax so that
    /// interpolations and lambdas are preserved exactly.
    /// </summary>
    private static RenderNode? TryAnalyzeExpression(
        ExpressionSyntax expr,
        SemanticModel semanticModel,
        KnownSymbols symbols,
        CancellationToken ct)
    {
        if (expr is not InvocationExpressionSyntax invocation)
            return null;

        var symbolInfo = semanticModel.GetSymbolInfo(invocation, ct);
        if (symbolInfo.Symbol is not IMethodSymbol method)
            return null;

        if (SymbolEqualityComparer.Default.Equals(method, symbols.TextMethod))
        {
            // Text(string content)
            var contentArg = invocation.ArgumentList.Arguments[0].Expression;
            return new TextNode(ContentExpression: contentArg.ToString());
        }

        if (SymbolEqualityComparer.Default.Equals(method, symbols.ButtonMethod))
        {
            // Button(string label, Action onClick)
            var labelArg = invocation.ArgumentList.Arguments[0].Expression;
            var handlerArg = invocation.ArgumentList.Arguments[1].Expression;
            return new ButtonNode(
                LabelExpression: labelArg.ToString(),
                HandlerExpression: handlerArg.ToString());
        }

        if (SymbolEqualityComparer.Default.Equals(method, symbols.VStackMethod))
        {
            // VStack(params View[] children) — expanded call form: each argument is a child
            var children = new List<RenderNode>(invocation.ArgumentList.Arguments.Count);
            foreach (var arg in invocation.ArgumentList.Arguments)
            {
                var childNode = TryAnalyzeExpression(arg.Expression, semanticModel, symbols, ct);
                if (childNode is null)
                    return null; // opaque child; fall back to null for the whole VStack
                children.Add(childNode);
            }
            return new VStackNode(children.ToImmutableArray());
        }

        if (SymbolEqualityComparer.Default.Equals(method, symbols.IfMethod))
        {
            // If(bool condition, Func<View> then, Func<View>? otherwise)
            var condArg = invocation.ArgumentList.Arguments[0].Expression;
            var thenArg = invocation.ArgumentList.Arguments[1].Expression;

            var thenExpr = ExtractLambdaBody(thenArg);
            if (thenExpr is null)
                return null;

            var thenNode = TryAnalyzeExpression(thenExpr, semanticModel, symbols, ct);
            if (thenNode is null)
                return null;

            RenderNode? otherwiseNode = null;
            if (invocation.ArgumentList.Arguments.Count >= 3)
            {
                var otherwiseArg = invocation.ArgumentList.Arguments[2].Expression;
                // A literal null argument means "no else branch".
                if (otherwiseArg is not LiteralExpressionSyntax { Token: { RawKind: (int)SyntaxKind.NullKeyword } })
                {
                    var otherwiseExpr = ExtractLambdaBody(otherwiseArg);
                    if (otherwiseExpr is null)
                        return null;

                    otherwiseNode = TryAnalyzeExpression(otherwiseExpr, semanticModel, symbols, ct);
                    if (otherwiseNode is null)
                        return null;
                }
            }

            return new IfNode(
                ConditionExpression: condArg.ToString(),
                Then: thenNode,
                Otherwise: otherwiseNode);
        }

        // Expressions that cannot be statically analyzed fall through here.
        // When the Opaque/BC2001 path is implemented, this will emit a runtime-evaluated
        // RenderFragment region and report BC2001.  Until then, returning null causes CS0534
        // in the user's compilation, which is the correct failure signal.

        return null;
    }

    /// <summary>
    /// Extracts the expression from a zero-parameter or parameterless lambda expression body,
    /// or returns <see langword="null"/> when the shape is not a simple expression lambda.
    /// </summary>
    private static ExpressionSyntax? ExtractLambdaBody(ExpressionSyntax expr) => expr switch
    {
        ParenthesizedLambdaExpressionSyntax { Body: ExpressionSyntax body } => body,
        SimpleLambdaExpressionSyntax { Body: ExpressionSyntax body } => body,
        _ => null,
    };
}
