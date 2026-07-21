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

        if (!InheritsFromComposeComponentBase(symbol))
            return null;

        var namespaceName = symbol.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString()
            : null;

        return new ComponentModel(
            HintName: $"{symbol.Name}.g.cs",
            ClassName: symbol.Name,
            Namespace: namespaceName);
    }

    private static bool InheritsFromComposeComponentBase(INamedTypeSymbol symbol)
    {
        var current = symbol.BaseType;
        while (current is not null)
        {
            if (current.Name == "ComposeComponentBase" &&
                current.ContainingNamespace is { IsGlobalNamespace: false, Name: "BlazorCompose" } ns &&
                ns.ContainingNamespace.IsGlobalNamespace)
            {
                return true;
            }
            current = current.BaseType;
        }
        return false;
    }
}
