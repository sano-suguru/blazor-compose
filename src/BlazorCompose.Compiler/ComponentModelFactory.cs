using System.Collections.Immutable;
using System.Threading;
using BlazorCompose.Compiler.Analysis;
using BlazorCompose.Compiler.Diagnostics;
using BlazorCompose.Compiler.Generation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCompose.Compiler;

/// <summary>Creates a <see cref="ComponentModelResult"/> from a syntax context for a candidate class node.</summary>
internal static class ComponentModelFactory
{
    /// <summary>
    /// Models <paramref name="syntaxContext"/> when it represents a partial class that directly or
    /// indirectly inherits from <c>BlazorCompose.ComposeComponentBase</c>.  The <c>Body</c> expression is
    /// analyzed into a template through <see cref="RenderExpressionAnalyzer"/> — the same SSC classifier
    /// used for composable definitions — and then expanded through <see cref="ComposableExpander"/>.  The
    /// returned result carries either a final <see cref="ComponentModel"/> or the call-site BC1002
    /// diagnostics produced during expansion.
    /// </summary>
    internal static ComponentModelResult TryCreate(
        GeneratorSyntaxContext syntaxContext,
        KnownSymbols? knownSymbols,
        ComposableRegistry registry,
        CancellationToken cancellationToken)
    {
        var classDeclaration = (ClassDeclarationSyntax)syntaxContext.Node;

        if (!classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            return ComponentModelResult.None;

        var symbol = syntaxContext.SemanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken);
        if (symbol is null)
            return ComponentModelResult.None;

        // Nested classes require wrapping the generated code inside the outer class hierarchy;
        // that complexity is out of scope for this task.  Skip them so the generator does not
        // emit a structurally incorrect top-level partial class.
        if (symbol.ContainingType is not null)
            return ComponentModelResult.None;

        if (!ComposeComponentBaseFacts.InheritsFromComposeComponentBase(symbol))
            return ComponentModelResult.None;

        // Without known symbols the Body cannot be analyzed; skip rather than emit empty source.
        if (knownSymbols is null)
            return ComponentModelResult.None;

        var bodyExpression = TryFindBodyExpression(classDeclaration);
        if (bodyExpression is null)
            return ComponentModelResult.None;

        // Reuse the composable-definition analyzer so component bodies and composable bodies share a
        // single SSC classification.  The component body has no parameters, so no parameter holes exist;
        // its access-requirement and diagnostic accumulators are irrelevant here because the generated
        // RenderBody is emitted directly into this same component type.
        var bodyContext = new ComposableBodyContext(
            syntaxContext.SemanticModel,
            symbol,
            "Body",
            knownSymbols,
            ImmutableDictionary.Create<ISymbol, int>(SymbolEqualityComparer.Default),
            cancellationToken);

        var template = RenderExpressionAnalyzer.Analyze(bodyExpression, bodyContext);

        // Body normalization can record BC1002 for a reference that cannot exist in the using-less
        // generated RenderBody (for example a null-conditional extension receiver that cannot be
        // rewritten to a static call).  Surface those diagnostics and suppress emission instead of
        // discarding them and emitting a broken RenderBody.
        if (bodyContext.Diagnostics.Count > 0)
            return new ComponentModelResult(null, bodyContext.Diagnostics.ToImmutable());

        // An unrecognized or unsupported Body shape must not produce an empty RenderBody; returning a
        // model-less result here causes CS0534 in the user's compilation, which is the correct failure
        // signal until the Opaque/BC2001 path is implemented.
        if (template is null)
            return ComponentModelResult.None;

        // Pass the generated component's inheritance chain (self first, then base types) so the expander
        // can validate DerivedContainingType access requirements against real inheritance rather than a
        // single containing-type key, while keeping the value model symbol-free.
        var inheritanceKeys = BuildInheritanceKeys(symbol);
        var expansion = ComposableExpander.Expand(template, registry, inheritanceKeys);

        // Call-site expansion failures surface as BC1002; do not also emit a partial RenderBody.
        if (!expansion.Diagnostics.IsDefaultOrEmpty)
            return new ComponentModelResult(null, expansion.Diagnostics);

        if (expansion.Node is null)
            return ComponentModelResult.None;

        var namespaceName = symbol.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString()
            : null;

        // Include namespace in the hint name to prevent collisions when two components share
        // the same simple class name across different namespaces.
        var hintName = namespaceName is not null
            ? $"{namespaceName}.{symbol.MetadataName}.g.cs"
            : $"{symbol.MetadataName}.g.cs";

        var model = new ComponentModel(
            HintName: hintName,
            ClassName: symbol.Name,
            Namespace: namespaceName,
            RootNode: expansion.Node);

        return new ComponentModelResult(model, []);
    }

    /// <summary>
    /// Returns the generated component's inheritance chain as fully qualified type keys, most-derived
    /// first (the component itself), then each base type up the hierarchy.  This is the symbol-free datum
    /// the expander uses to validate protected/private-protected access requirements.
    /// </summary>
    private static ImmutableArray<string> BuildInheritanceKeys(INamedTypeSymbol symbol)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        for (INamedTypeSymbol? current = symbol; current is not null; current = current.BaseType)
            builder.Add(current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

        return builder.ToImmutable();
    }

    /// <summary>
    /// Returns the expression of the component's expression-bodied <c>Body</c> override
    /// (<c>protected override View Body =&gt; expr;</c>), or <see langword="null"/> when no such property
    /// is present.  Block-bodied getters are intentionally unsupported.
    /// </summary>
    private static ExpressionSyntax? TryFindBodyExpression(ClassDeclarationSyntax classDecl)
    {
        foreach (var member in classDecl.Members)
        {
            if (member is not PropertyDeclarationSyntax prop)
                continue;

            if (prop.Identifier.Text != "Body")
                continue;

            if (!prop.Modifiers.Any(SyntaxKind.OverrideKeyword))
                continue;

            if (prop.ExpressionBody is { Expression: var bodyExpr })
                return bodyExpr;
        }

        return null;
    }
}
