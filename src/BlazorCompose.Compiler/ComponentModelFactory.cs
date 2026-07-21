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
    /// class that directly or indirectly inherits from <c>BlazorCompose.ComposeComponentBase</c>,
    /// or <see langword="null"/> otherwise.
    /// </summary>
    internal static ComponentModel? TryCreate(GeneratorSyntaxContext syntaxContext, CancellationToken cancellationToken)
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

        var namespaceName = symbol.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString()
            : null;

        // Include namespace in the hint name to prevent collisions when two components share
        // the same simple class name across different namespaces.
        var hintName = namespaceName is not null
            ? $"{namespaceName}.{symbol.MetadataName}.g.cs"
            : $"{symbol.MetadataName}.g.cs";

        var knownSymbols = KnownSymbols.TryCreate(syntaxContext.SemanticModel.Compilation);
        var rootNode = knownSymbols is not null
            ? TryExtractBodyNode(classDeclaration, syntaxContext.SemanticModel, knownSymbols, cancellationToken)
            : null;

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

        // IfNode recognition without allocation/emission (Task 5 owns those).
        // Return null so the generator emits an empty RenderBody rather than incorrect code.

        return null;
    }
}
