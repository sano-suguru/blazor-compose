using System.Threading;
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
            ? $"{namespaceName}.{symbol.Name}.g.cs"
            : $"{symbol.Name}.g.cs";

        return new ComponentModel(
            HintName: hintName,
            ClassName: symbol.Name,
            Namespace: namespaceName);
    }
}
